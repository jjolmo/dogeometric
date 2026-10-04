using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's Push/Pull. A face whose every edge borders a face perpendicular to it (the side of a box) just
/// moves, stretching its neighbours. Otherwise the face is extruded: side faces join the old outline to the moved
/// copy, and side faces that end up coplanar with a neighbour merge with it. With <c>keepBase</c> (Ctrl) the
/// original face stays and a new one is extruded from it.
/// </summary>
public static class PushPull
{
    public static Face Apply(Entities e, Face face, double distance, bool keepBase = false)
    {
        var normal = face.Normal;
        var offset = normal * distance;
        if (Math.Abs(distance) <= Tolerance.Length)
            return face;

        // A face with no neighbours (a rectangle on its own) keeps itself as the base of the new volume, as SketchUp
        // turns a lone rectangle into a closed box.
        keepBase |= Topology.EdgesOf(face).All(edge => Topology.FacesOf(e, edge).All(f => f == face));

        if (!keepBase && CanSlide(e, face, normal))
        {
            foreach (var v in face.Loops.SelectMany(l => l.Vertices).Distinct())
                v.Position += offset;
            return face;
        }

        // Extrude: a copy of the face at the offset, joined to the original outline by side faces.
        var copies = new Dictionary<Vertex, Vertex>();
        Vertex Moved(Vertex v)
        {
            if (!copies.TryGetValue(v, out var c))
                copies[v] = c = e.AddVertex(v.Position + offset);
            return c;
        }

        var cap = new Face { FrontMaterial = face.FrontMaterial, BackMaterial = face.BackMaterial, Tag = face.Tag };
        var sides = new List<Face>();
        foreach (var loop in face.Loops)
        {
            var capLoop = new FaceLoop();
            foreach (var (edge, reversed) in loop.Edges)
            {
                var a = reversed ? edge.End : edge.Start;
                var b = reversed ? edge.Start : edge.End;
                var a2 = Moved(a);
                var b2 = Moved(b);
                var top = e.EdgeBetween(a2, b2);
                top.Curve ??= edge.Curve;
                capLoop.Edges.Add((top, top.Start != a2));

                // a→b→b2→a2 has normal distance·(b−a)×n: away from the face's interior when pulling, towards it when
                // pushing a pocket. A kept base pushed backwards builds the volume behind it, so the walls flip.
                var quad = keepBase && distance < 0 ? new[] { a, a2, b2, b } : [a, b, b2, a2];
                var side = new Face { Tag = face.Tag };
                var sideLoop = new FaceLoop();
                for (var i = 0; i < 4; i++)
                {
                    var p = quad[i];
                    var q = quad[(i + 1) % 4];
                    var edgeAB = e.EdgeBetween(p, q);
                    sideLoop.Edges.Add((edgeAB, edgeAB.Start != p));
                }
                side.Loops.Add(sideLoop);
                sides.Add(side);
            }
            cap.Loops.Add(capLoop);
        }

        // Pushing (distance < 0) makes the moved copy the bottom of the new volume: it faces the other way.
        if (distance < 0 && keepBase)
            FaceFinder.Reverse(cap);

        // Edges rising from inside a curve (between two of its segments) are soft and smooth, so an extruded circle
        // looks like a cylinder, as in SketchUp.
        foreach (var loop in face.Loops)
        {
            for (var i = 0; i < loop.Edges.Count; i++)
            {
                var prev = loop.Edges[(i - 1 + loop.Edges.Count) % loop.Edges.Count].Edge;
                var cur = loop.Edges[i].Edge;
                if (cur.Curve is { IsPolygon: false } c && prev.Curve == c)
                {
                    var corner = loop.Edges[i].Reversed ? cur.End : cur.Start;
                    var riser = e.EdgeBetween(corner, Moved(corner));
                    riser.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
                }
            }
        }

        e.Faces.Add(cap);
        e.Faces.AddRange(sides);
        if (!keepBase)
            e.Faces.Remove(face);
        else if (distance > 0)
            FaceFinder.Reverse(face); // the kept base now closes the volume from below

        MergeCoplanarNeighbours(e, sides);
        if (!keepBase && PunchThrough(e, cap))
            cap = null!;
        Editing.RemoveOrphanVertices(e);
        return cap;
    }

    /// <summary>
    /// Pushing a face until it meets a parallel face on the far side cuts through, as in SketchUp: the cap and
    /// the part of the far face under it disappear, leaving a hole. Returns true when it cut.
    /// </summary>
    private static bool PunchThrough(Entities e, Face cap)
    {
        var n = cap.Normal;
        var d = n.Dot(cap.OuterLoop.Points.First());
        var centre = cap.OuterLoop.Points.Aggregate(Vec3.Zero, (a, p) => a + p) / cap.OuterLoop.Edges.Count;
        var far = e.Faces.FirstOrDefault(f => f != cap
            && f.Normal.Dot(n) < -1 + 1e-9
            && Math.Abs(n.Dot(f.OuterLoop.Points.First()) - d) <= 1e-3
            && Inside(f, centre));
        if (far == null)
            return false;

        var capEdges = Topology.EdgesOf(cap).ToHashSet();
        e.Faces.Remove(cap);
        // Imprint the cap's outline on the far face, then drop the region it encloses.
        var before = e.Faces.ToHashSet();
        FaceFinder.Update(e, capEdges.ToList());
        var plug = e.Faces.FirstOrDefault(f => !before.Contains(f) && Topology.EdgesOf(f).All(capEdges.Contains));
        if (plug != null)
            e.Faces.Remove(plug);
        return true;
    }

    private static bool Inside(Face f, Vec3 p)
    {
        var (u, v) = Polygon.PlaneAxes(f.Normal);
        bool In(IEnumerable<Vec3> loop)
        {
            var poly = loop.Select(q => (X: q.Dot(u), Y: q.Dot(v))).ToList();
            var (px, py) = (p.Dot(u), p.Dot(v));
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > py) != (poly[j].Y > py) && px < (poly[j].X - poly[i].X) * (py - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }
            return inside;
        }
        return In(f.OuterLoop.Points) && !f.InnerLoops.Any(l => In(l.Points));
    }

    /// <summary>True when every edge of the face borders exactly one other face, perpendicular to it.</summary>
    private static bool CanSlide(Entities e, Face face, Vec3 normal)
    {
        foreach (var edge in Topology.EdgesOf(face))
        {
            var others = Topology.FacesOf(e, edge).Where(f => f != face).ToList();
            if (others.Count != 1 || Math.Abs(others[0].Normal.Dot(normal)) > 1e-6)
                return false;
        }
        return true;
    }

    /// <summary>Heals new side faces into coplanar neighbours with the same orientation and materials.</summary>
    private static void MergeCoplanarNeighbours(Entities e, IEnumerable<Face> candidates)
    {
        var queue = new Queue<Face>(candidates);
        while (queue.Count > 0)
        {
            var f = queue.Dequeue();
            if (!e.Faces.Contains(f))
                continue;
            foreach (var edge in Topology.EdgesOf(f).ToList())
            {
                var other = Topology.FacesOf(e, edge).FirstOrDefault(o => o != f);
                if (other == null || Topology.FacesOf(e, edge).Count() != 2)
                    continue;
                if (other.Normal.Dot(f.Normal) < 1 - 1e-9 || !SamePlane(f, other))
                    continue;
                if (other.FrontMaterial != f.FrontMaterial || other.BackMaterial != f.BackMaterial)
                    continue;
                if (FaceFinder.Merge(e, f, other, edge) is { } merged)
                    queue.Enqueue(merged);
                break;
            }
        }
    }

    private static bool SamePlane(Face a, Face b)
    {
        var n = a.Normal;
        var d = n.Dot(a.OuterLoop.Points.First());
        return b.Loops.SelectMany(l => l.Points).All(p => Math.Abs(n.Dot(p) - d) <= 1e-3);
    }
}
