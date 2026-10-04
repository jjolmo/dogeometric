using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's Follow Me: sweeps a profile face along a path of connected edges. The profile is carried along each
/// path segment and cut by the bisecting (mitre) plane at every path vertex, so a polygonal path gives mitred
/// joints. An open path keeps the profile as the start cap and closes the far end; a closed path (a loop, such as
/// a circle for a lathe) has no caps and the profile is consumed. Edges inside a curve come out soft and smooth.
/// </summary>
public static class FollowMe
{
    /// <summary>
    /// Sweeps <paramref name="profile"/> along <paramref name="pathEdges"/> (a chain or a loop, in any order).
    /// Returns the new faces, or an empty list when the edges don't form a single path.
    /// </summary>
    public static List<Face> Apply(Entities e, Face profile, IReadOnlyCollection<Edge> pathEdges)
    {
        var profileEdges = Topology.EdgesOf(profile).ToHashSet();
        var path = OrderPath(pathEdges.Where(x => !profileEdges.Contains(x)).ToList(), Centroid(profile));
        if (path == null)
            return [];
        var (vertices, edges, closed) = path.Value;
        var points = vertices.Select(v => v.Position).ToList();
        if (points.Count < 2)
            return [];

        var dirs = new List<Vec3>();
        for (var i = 0; i < edges.Count; i++)
            dirs.Add((points[(i + 1) % points.Count] - points[i]).Normalized());

        // Orient the profile so its loops wind counter-clockwise around the sweep direction: side faces then face out.
        var start = profile.Normal.Dot(dirs[0]) >= 0;
        var loops = profile.Loops.Select(l => l.Points.ToList()).ToList();
        if (!start)
            foreach (var l in loops)
                l.Reverse();

        // Ring 0: the profile itself (open path) or the profile carried onto the start mitre (closed path).
        var rings = new List<List<List<Vec3>>>();
        var segments = edges.Count;
        if (closed)
        {
            var n0 = (dirs[^1] + dirs[0]).Normalized();
            rings.Add(loops.Select(l => l.Select(p => OntoPlane(p, dirs[0], points[0], n0)).ToList()).ToList());
        }
        else
        {
            rings.Add(loops);
        }
        for (var k = 1; k <= segments; k++)
        {
            if (closed && k == segments)
                break; // the last ring is ring 0
            var normal = !closed && k == segments ? dirs[k - 1] : (dirs[k - 1] + dirs[k % segments]).Normalized();
            var at = points[k % points.Count];
            rings.Add(rings[k - 1].Select(l => l.Select(p => OntoPlane(p, dirs[k - 1], at, normal)).ToList()).ToList());
        }

        // Which ring edges / longitudinal edges sit inside a curve and should be soft.
        bool PathSmoothAt(int k) => (closed || (k > 0 && k < segments)) &&
            edges[(k - 1 + segments) % segments].Curve is { IsPolygon: false } c && edges[k % segments].Curve == c;
        var profileSmooth = profile.Loops.Select(l =>
        {
            var list = l.Edges;
            return Enumerable.Range(0, list.Count).Select(j =>
            {
                var prev = list[(j - 1 + list.Count) % list.Count].Edge.Curve;
                return prev is { IsPolygon: false } && list[j].Edge.Curve == prev;
            }).ToList();
        }).ToList();
        if (!start)
        {
            // Vertex j of a reversed loop is vertex count − 1 − j of the original.
            profileSmooth = profileSmooth.Select(f => Enumerable.Range(0, f.Count).Select(j => f[f.Count - 1 - j]).ToList()).ToList();
        }

        var created = new List<Face>();
        for (var k = 0; k < segments; k++)
        {
            var a = rings[k];
            var b = rings[(k + 1) % rings.Count];
            for (var li = 0; li < a.Count; li++)
            {
                var la = a[li];
                var lb = b[li];
                for (var j = 0; j < la.Count; j++)
                {
                    var j2 = (j + 1) % la.Count;
                    Vec3[] quad = [la[j], la[j2], lb[j2], lb[j]];
                    if (Polygon.Area(quad) <= 1e-9)
                        continue;
                    var face = e.AddFace(quad);
                    face.Tag = profile.Tag;
                    face.FrontMaterial = profile.FrontMaterial;
                    created.Add(face);
                    if (profileSmooth[li][j])
                        Soften(e.EdgeBetween(e.VertexAt(la[j]), e.VertexAt(lb[j])));
                }
                if (PathSmoothAt(k))
                    for (var j = 0; j < la.Count; j++)
                        Soften(e.EdgeBetween(e.VertexAt(la[j]), e.VertexAt(la[(j + 1) % la.Count])));
            }
        }

        if (closed)
        {
            // The profile is consumed; its edges go too unless the sweep reused them.
            e.Faces.Remove(profile);
            foreach (var edge in profileEdges)
                if (!Topology.FacesOf(e, edge).Any())
                    e.Edges.Remove(edge);
        }
        else
        {
            // The profile closes the start, facing back along the path; a copy closes the far end.
            if (start)
                FaceFinder.Reverse(profile);
            var end = rings[^1];
            var cap = e.AddFace(end[0], end.Skip(1).Select(l => (IReadOnlyList<Vec3>)l).ToList());
            cap.Tag = profile.Tag;
            cap.FrontMaterial = profile.FrontMaterial;
            created.Add(cap);
        }
        Editing.RemoveOrphanVertices(e);
        return created;
    }

    private static void Soften(Edge edge) => edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;

    private static Vec3 Centroid(Face f) => f.OuterLoop.Points.Aggregate(Vec3.Zero, (s, p) => s + p) / f.OuterLoop.Edges.Count;

    /// <summary>Moves <paramref name="p"/> along <paramref name="dir"/> onto the plane (point, normal).</summary>
    private static Vec3 OntoPlane(Vec3 p, Vec3 dir, Vec3 point, Vec3 normal)
    {
        var denom = dir.Dot(normal);
        if (Math.Abs(denom) < 1e-9)
            return p;
        return p + dir * ((point - p).Dot(normal) / denom);
    }

    /// <summary>
    /// Orders edges into a chain or loop. An open chain starts at the end nearer <paramref name="near"/>; a loop
    /// starts at the vertex nearest it. Null if the edges branch or fall apart.
    /// </summary>
    public static (List<Vertex> Vertices, List<Edge> Edges, bool Closed)? OrderPath(List<Edge> edges, Vec3 near)
    {
        if (edges.Count == 0)
            return null;
        var byVertex = new Dictionary<Vertex, List<Edge>>();
        foreach (var edge in edges)
        {
            foreach (var v in new[] { edge.Start, edge.End })
            {
                if (!byVertex.TryGetValue(v, out var list))
                    byVertex[v] = list = [];
                list.Add(edge);
            }
        }
        if (byVertex.Values.Any(l => l.Count > 2))
            return null;

        var ends = byVertex.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).ToList();
        var closed = ends.Count == 0;
        if (!closed && ends.Count != 2)
            return null;
        var first = closed
            ? byVertex.Keys.MinBy(v => v.Position.DistanceTo(near))!
            : ends.MinBy(v => v.Position.DistanceTo(near))!;

        var vertices = new List<Vertex> { first };
        var ordered = new List<Edge>();
        var current = first;
        Edge? previous = null;
        while (true)
        {
            var next = byVertex[current].FirstOrDefault(x => x != previous && !ordered.Contains(x));
            if (next == null)
                break;
            ordered.Add(next);
            current = next.Other(current);
            previous = next;
            if (current == first)
                break;
            vertices.Add(current);
        }
        if (ordered.Count != edges.Count)
            return null;
        return (vertices, ordered, closed);
    }
}
