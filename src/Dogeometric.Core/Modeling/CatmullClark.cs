using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SUbD's subdivision (Catmull-Clark): every face becomes quads around a face point, rounding the mesh off; edges with
/// one face (open borders) are kept as creases. The result is soft and smooth, as SUbD shows it.
/// </summary>
public static class CatmullClark
{
    /// <summary>Subdivides every face of <paramref name="e"/> <paramref name="levels"/> times; returns the faces made.</summary>
    public static int Apply(Entities e, int levels)
    {
        if (e.Faces.Count == 0 || levels <= 0)
            return 0;
        var points = new List<Vec3>();
        var index = new Dictionary<Vertex, int>();
        var polys = new List<(int[] Corners, Face Source)>();
        foreach (var f in e.Faces.Where(f => f.Loops.Count == 1))
        {
            var corners = f.OuterLoop.Vertices.Select(v =>
            {
                if (!index.TryGetValue(v, out var i))
                {
                    index[v] = i = points.Count;
                    points.Add(v.Position);
                }
                return i;
            }).ToArray();
            polys.Add((corners, f));
        }

        for (var level = 0; level < levels; level++)
            (points, polys) = Step(points, polys);

        var sources = e.Faces.Where(f => f.Loops.Count == 1).ToHashSet();
        var own = sources.SelectMany(Topology.EdgesOf).ToHashSet();
        e.Faces.RemoveAll(sources.Contains);
        e.Edges.RemoveAll(own.Contains);
        Editing.RemoveOrphanVertices(e);
        var weld = new Welder(e);
        var made = new List<Face>();
        foreach (var (corners, src) in polys)
        {
            var pts = corners.Select(i => points[i]).ToList();
            // Subdivided quads are seldom flat: two triangles each, like SUbD's output mesh.
            foreach (var tri in Flat(pts) ? [pts] : new List<List<Vec3>> { new() { pts[0], pts[1], pts[2] }, new() { pts[0], pts[2], pts[3] } })
            {
                var f = weld.Face(tri, []);
                f.FrontMaterial = src.FrontMaterial;
                f.BackMaterial = src.BackMaterial;
                f.Tag = src.Tag;
                if (f.Normal.Dot(Polygon.Normal(pts)) < 0)
                    FaceFinder.Reverse(f);
                made.Add(f);
            }
        }
        foreach (var edge in made.SelectMany(Topology.EdgesOf).Distinct())
            if (Topology.FacesOf(e, edge).Count() == 2)
                edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        return made.Count;
    }

    private static bool Flat(List<Vec3> pts)
    {
        if (pts.Count <= 3)
            return true;
        var n = Polygon.Normal(pts);
        return pts.All(p => Math.Abs((p - pts[0]).Dot(n)) < Tolerance.Length);
    }

    /// <summary>One Catmull-Clark round over polygons given as point indices.</summary>
    private static (List<Vec3>, List<(int[], Face)>) Step(List<Vec3> points, List<(int[] Corners, Face Source)> polys)
    {
        var facePoint = polys.Select(p => p.Corners.Aggregate(Vec3.Zero, (a, i) => a + points[i]) / p.Corners.Length).ToList();
        var edgeFaces = new Dictionary<(int, int), List<int>>();
        for (var f = 0; f < polys.Count; f++)
        {
            var c = polys[f].Corners;
            for (var k = 0; k < c.Length; k++)
            {
                var key = Key(c[k], c[(k + 1) % c.Length]);
                if (!edgeFaces.TryGetValue(key, out var list))
                    edgeFaces[key] = list = [];
                list.Add(f);
            }
        }

        var next = new List<Vec3>(points);
        var edgePoint = new Dictionary<(int, int), int>();
        foreach (var ((a, b), faces) in edgeFaces)
        {
            var p = faces.Count == 2
                ? (points[a] + points[b] + facePoint[faces[0]] + facePoint[faces[1]]) / 4
                : (points[a] + points[b]) / 2;
            edgePoint[(a, b)] = next.Count;
            next.Add(p);
        }

        // Each original vertex moves to (F + 2R + (n-3)P) / n, or along its border for crease (border) vertices.
        var vertexFaces = new Dictionary<int, List<int>>();
        var vertexEdges = new Dictionary<int, List<(int, int)>>();
        for (var f = 0; f < polys.Count; f++)
            foreach (var v in polys[f].Corners)
            {
                if (!vertexFaces.TryGetValue(v, out var list))
                    vertexFaces[v] = list = [];
                list.Add(f);
            }
        foreach (var key in edgeFaces.Keys)
            foreach (var v in new[] { key.Item1, key.Item2 })
            {
                if (!vertexEdges.TryGetValue(v, out var list))
                    vertexEdges[v] = list = [];
                list.Add(key);
            }
        foreach (var (v, faces) in vertexFaces)
        {
            var edges = vertexEdges[v];
            var border = edges.Where(k => edgeFaces[k].Count != 2).ToList();
            // Corners (only two edges) stay where they are.
            if (edges.Count == 2)
                continue;
            if (border.Count >= 2)
            {
                var ends = border.Take(2).Select(k => points[k.Item1 == v ? k.Item2 : k.Item1]).ToList();
                next[v] = points[v] * 0.75 + (ends[0] + ends[1]) * 0.125;
                continue;
            }
            var n = faces.Count;
            var f = faces.Aggregate(Vec3.Zero, (acc, i) => acc + facePoint[i]) / n;
            var r = edges.Aggregate(Vec3.Zero, (acc, k) => acc + (points[k.Item1] + points[k.Item2]) / 2) / edges.Count;
            next[v] = (f + r * 2 + points[v] * (n - 3)) / n;
        }

        var faceIndex = new int[polys.Count];
        for (var f = 0; f < polys.Count; f++)
        {
            faceIndex[f] = next.Count;
            next.Add(facePoint[f]);
        }

        var quads = new List<(int[], Face)>();
        for (var f = 0; f < polys.Count; f++)
        {
            var c = polys[f].Corners;
            for (var k = 0; k < c.Length; k++)
            {
                var prev = c[(k - 1 + c.Length) % c.Length];
                var here = c[k];
                var after = c[(k + 1) % c.Length];
                quads.Add(([here, edgePoint[Key(here, after)], faceIndex[f], edgePoint[Key(prev, here)]], polys[f].Source));
            }
        }
        return (next, quads);
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}
