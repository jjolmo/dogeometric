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

        if (!keepBase && CanSlide(e, face, normal, distance))
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
        CancelOpposedOverlaps(e, sides);
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

    private static bool Inside(Face f, Vec3 p) => Inside(f.Normal, f.Loops.Select(l => l.Points.ToList()).ToList(), p);

    /// <summary>Whether <paramref name="p"/> lies inside the outline <c>loops[0]</c> and outside its holes.</summary>
    private static bool Inside(Vec3 normal, IReadOnlyList<List<Vec3>> loops, Vec3 p)
    {
        var (u, v) = Polygon.PlaneAxes(normal);
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
        return In(loops[0]) && !loops.Skip(1).Any(In);
    }

    /// <summary>
    /// True when every edge of the face borders exactly one other face, perpendicular to it, that can stretch: one
    /// lying across the face's plane (a top face running past the end of a bar on it) would fold over itself.
    /// </summary>
    private static bool CanSlide(Entities e, Face face, Vec3 normal, double distance)
    {
        var d = normal.Dot(face.OuterLoop.Points.First());
        foreach (var edge in Topology.EdgesOf(face))
        {
            var others = Topology.FacesOf(e, edge).Where(f => f != face).ToList();
            if (others.Count != 1 || Math.Abs(others[0].Normal.Dot(normal)) > 1e-6)
                return false;
            var heights = others[0].Loops.SelectMany(l => l.Points).Select(p => normal.Dot(p) - d).ToList();
            var (low, high) = (heights.Min(), heights.Max());
            var behind = high <= Tolerance.Length;
            if (!behind && low < -Tolerance.Length)
                return false;
            // Moving towards the neighbour shrinks it, which only works while it has length left.
            var room = behind ? -low : high;
            if ((behind ? distance < 0 : distance > 0) && Math.Abs(distance) >= room - Tolerance.Length)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A new side face lying on an existing face that faces the other way (the underside of a bar pulled out past
    /// the top it stands on) cancels with it where they overlap, as that part is now inside the solid.
    /// </summary>
    private static void CancelOpposedOverlaps(Entities e, IEnumerable<Face> candidates)
    {
        foreach (var s in candidates.ToList())
        {
            if (!e.Faces.Contains(s))
                continue;
            var t = e.Faces.FirstOrDefault(f => f != s && f.Normal.Dot(s.Normal) < -1 + 1e-9 && SamePlane(s, f)
                && (Samples(s).Any(p => Inside(f, p)) || Samples(f).Any(p => Inside(s, p))));
            if (t == null)
                continue;

            var n = s.Normal;
            var plane = n.Dot(s.OuterLoop.Points.First());
            bool InPlane(Vec3 p) => Math.Abs(n.Dot(p) - plane) <= 1e-3;
            var shapes = new[] { s, t }.Select(f => (Face: f, Loops: f.Loops.Select(l => l.Points.ToList()).ToList())).ToList();
            var outlines = Topology.EdgesOf(s).Concat(Topology.EdgesOf(t)).Select(x => (x.Start.Position, x.End.Position)).ToList();
            e.Faces.Remove(s);
            e.Faces.Remove(t);

            // Both outlines into one planar graph: crossings and touching corners become shared vertices.
            foreach (var (a, b) in outlines)
                StickyGeometry.AddSegment(e, a, b);
            SplitAtVertices(e, InPlane);
            MergeDuplicateEdges(e);

            var planeEdges = e.Edges.Where(x => InPlane(x.Start.Position) && InPlane(x.End.Position)).ToList();
            var before = e.Faces.ToHashSet();
            FaceFinder.Update(e, planeEdges, n);
            foreach (var f in e.Faces.Where(f => !before.Contains(f) && Math.Abs(f.Normal.Dot(n)) > 1 - 1e-9 && f.OuterLoop.Points.All(InPlane)).ToList())
            {
                var p = Samples(f).FirstOrDefault(q => Inside(f, q));
                var owners = shapes.Where(x => Inside(n, x.Loops, p)).ToList();
                if (owners.Count != 1)
                {
                    // Inside both (the overlap) or neither (a gap the outlines happen to close): no face there.
                    e.Faces.Remove(f);
                    continue;
                }
                var owner = owners[0].Face;
                if (f.Normal.Dot(owner.Normal) < 0)
                    FaceFinder.Reverse(f);
                (f.FrontMaterial, f.BackMaterial, f.Tag) = (owner.FrontMaterial, owner.BackMaterial, owner.Tag);
            }
            var unused = planeEdges.Where(x => e.Edges.Contains(x) && !Topology.FacesOf(e, x).Any()).ToHashSet();
            e.Edges.RemoveAll(unused.Contains);
            CleanUp.RepairSplitEdges(e, e.Edges.Where(x => InPlane(x.Start.Position) && InPlane(x.End.Position)).Cast<object>().ToHashSet());
        }
    }

    /// <summary>Points just inside the outline next to the middle of each of its edges.</summary>
    private static IEnumerable<Vec3> Samples(Face f)
    {
        var n = f.Normal;
        var pts = f.OuterLoop.Points.ToList();
        for (var i = 0; i < pts.Count; i++)
        {
            var (a, b) = (pts[i], pts[(i + 1) % pts.Count]);
            yield return (a + b) / 2 + n.Cross(b - a).Normalized() * 0.01;
        }
    }

    /// <summary>Splits edges in the plane wherever another vertex of it lies on them (where outlines overlap).</summary>
    private static void SplitAtVertices(Entities e, Func<Vec3, bool> inPlane)
    {
        var vertices = e.Vertices.Where(v => inPlane(v.Position)).ToList();
        var queue = new Queue<Edge>(e.Edges.Where(x => inPlane(x.Start.Position) && inPlane(x.End.Position)));
        while (queue.Count > 0)
        {
            var edge = queue.Dequeue();
            var (a, b) = (edge.Start.Position, edge.End.Position);
            var dir = b - a;
            var on = vertices.FirstOrDefault(v =>
            {
                var t = (v.Position - a).Dot(dir) / dir.LengthSquared;
                return v != edge.Start && v != edge.End && t > 0 && t < 1 && (a + dir * t).DistanceTo(v.Position) <= Tolerance.Length;
            });
            if (on == null)
                continue;
            var tail = StickyGeometry.SplitEdge(e, edge, on);
            queue.Enqueue(edge);
            queue.Enqueue(tail);
        }
    }

    /// <summary>Edges joining the same two vertices become one, which every face that used either now shares.</summary>
    private static void MergeDuplicateEdges(Entities e)
    {
        var kept = new Dictionary<(Vertex, Vertex), Edge>();
        foreach (var edge in e.Edges.ToList())
        {
            if (!kept.TryGetValue((edge.Start, edge.End), out var keep) && !kept.TryGetValue((edge.End, edge.Start), out keep))
            {
                kept[(edge.Start, edge.End)] = edge;
                continue;
            }
            var flip = keep.Start != edge.Start;
            foreach (var loop in e.Faces.SelectMany(f => f.Loops))
                for (var i = 0; i < loop.Edges.Count; i++)
                    if (loop.Edges[i].Edge == edge)
                        loop.Edges[i] = (keep, loop.Edges[i].Reversed ^ flip);
            e.Edges.Remove(edge);
        }
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
