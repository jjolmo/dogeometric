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

        e.Faces.Add(cap);
        e.Faces.AddRange(sides);
        if (!keepBase)
            e.Faces.Remove(face);
        else if (distance > 0)
            FaceFinder.Reverse(face); // the kept base now closes the volume from below

        MergeCoplanarNeighbours(e, sides);
        Editing.RemoveOrphanVertices(e);
        return cap;
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
