using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>SUbD's Catmull-Clark subdivision. Creased edges and vertices stay sharp for as many rounds as their
/// sharpness (blending for fractions, as OpenSubdiv does); open borders are always sharp.</summary>
public static class CatmullClark
{
    /// <summary>A subdivided mesh: polygons over points, each from a source face, and the edges that stay hard lines.</summary>
    public sealed record Result(List<Vec3> Points, List<(int[] Corners, Face Source)> Polygons, HashSet<(int, int)> Hard);

    private sealed record Level(
        List<Vec3> Points, List<(int[] Corners, Face Source)> Polygons,
        Dictionary<(int, int), double> EdgeSharpness, Dictionary<int, double> VertexSharpness, HashSet<(int, int)> Hard);

    /// <summary>The faces of <paramref name="e"/> subdivided <paramref name="levels"/> times, without changing them.</summary>
    public static Result Mesh(Entities e, int levels)
    {
        var points = new List<Vec3>();
        var index = new Dictionary<Vertex, int>();
        int I(Vertex v)
        {
            if (!index.TryGetValue(v, out var i))
            {
                index[v] = i = points.Count;
                points.Add(v.Position);
            }
            return i;
        }
        var polys = e.Faces.Where(f => f.Loops.Count == 1).Select(f => (f.OuterLoop.Vertices.Select(I).ToArray(), f)).ToList();
        var edgeSharpness = new Dictionary<(int, int), double>();
        var hard = new HashSet<(int, int)>();
        foreach (var edge in e.Edges.Where(x => x.Crease > 0 && index.ContainsKey(x.Start) && index.ContainsKey(x.End)))
        {
            var key = Key(index[edge.Start], index[edge.End]);
            edgeSharpness[key] = edge.Crease;
            hard.Add(key);
        }
        var vertexSharpness = index.Where(kv => kv.Key.Crease > 0).ToDictionary(kv => kv.Value, kv => kv.Key.Crease);

        var level = new Level(points, polys, edgeSharpness, vertexSharpness, hard);
        for (var i = 0; i < levels; i++)
            level = Step(level, e.SubdivisionSmoothCorners);
        return new Result(level.Points, level.Polygons, level.Hard);
    }

    /// <summary>The bounds of what <paramref name="e"/> shows: its subdivided surface when it has one.</summary>
    public static Bounds3 ShownBounds(Entities e) => e.Subdivision > 0 && e.Faces.Count > 0
        ? Mesh(e, e.Subdivision).Points.Aggregate(e.Instances.Count > 0 ? e.Bounds() : Bounds3.Empty, (b, p) => b.Include(p))
        : e.Bounds();

    /// <summary>Subdivides every face of <paramref name="e"/> <paramref name="levels"/> times; returns the faces made.</summary>
    public static int Apply(Entities e, int levels)
    {
        if (e.Faces.Count == 0 || levels <= 0)
            return 0;
        var (points, polys, hard) = Mesh(e, levels);

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
        var hardSegments = hard.Select(k => (points[k.Item1], points[k.Item2])).ToList();
        bool IsHard(Edge edge) => hardSegments.Any(s =>
            s.Item1.DistanceTo(edge.Start.Position) < Tolerance.Length && s.Item2.DistanceTo(edge.End.Position) < Tolerance.Length ||
            s.Item2.DistanceTo(edge.Start.Position) < Tolerance.Length && s.Item1.DistanceTo(edge.End.Position) < Tolerance.Length);
        foreach (var edge in made.SelectMany(Topology.EdgesOf).Distinct())
            if (Topology.FacesOf(e, edge).Count() == 2 && !IsHard(edge))
                edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        return made.Count;
    }

    public static bool Flat(List<Vec3> pts)
    {
        if (pts.Count <= 3)
            return true;
        var n = Polygon.Normal(pts);
        return pts.All(p => Math.Abs((p - pts[0]).Dot(n)) < Tolerance.Length);
    }

    private static Vec3 Lerp(Vec3 a, Vec3 b, double t) => a + (b - a) * t;

    /// <summary>One Catmull-Clark round with semi-sharp creases (DeRose et al. 1998).</summary>
    private static Level Step(Level level, bool smoothCorners)
    {
        var (points, polys, edgeSharpness, vertexSharpness, hard) = level;
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
        double Sharpness((int, int) key) => edgeFaces[key].Count != 2 ? double.PositiveInfinity : edgeSharpness.GetValueOrDefault(key);

        var next = new List<Vec3>(points);
        var edgePoint = new Dictionary<(int, int), int>();
        foreach (var ((a, b), faces) in edgeFaces)
        {
            var mid = (points[a] + points[b]) / 2;
            var s = Sharpness((a, b));
            var p = s >= 1 ? mid : Lerp((points[a] + points[b] + facePoint[faces[0]] + facePoint[faces[1]]) / 4, mid, s);
            edgePoint[(a, b)] = next.Count;
            next.Add(p);
        }

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
            // Corners of an open mesh (only two edges) stay where they are unless Boundary Corners is Smooth.
            if (!smoothCorners && edges.Count == 2 && edges.All(k => edgeFaces[k].Count != 2))
                continue;
            var n = faces.Count;
            var smooth = edges.Any(k => edgeFaces[k].Count != 2)
                ? points[v]
                : (faces.Aggregate(Vec3.Zero, (acc, i) => acc + facePoint[i]) / n
                   + edges.Aggregate(Vec3.Zero, (acc, k) => acc + (points[k.Item1] + points[k.Item2]) / 2) / edges.Count * 2
                   + points[v] * (n - 3)) / n;
            var sharp = edges.Where(k => Sharpness(k) > 0).ToList();
            var vs = vertexSharpness.GetValueOrDefault(v);
            Vec3 target;
            double weight;
            if (vs > 0 || sharp.Count > 2)
            {
                // A corner: stays put, fully once its sharpness reaches a round.
                target = points[v];
                weight = Math.Max(vs, sharp.Count > 2 ? sharp.Average(Sharpness) : 0);
            }
            else if (sharp.Count == 2)
            {
                var ends = sharp.Select(k => points[k.Item1 == v ? k.Item2 : k.Item1]).ToList();
                target = points[v] * 0.75 + (ends[0] + ends[1]) * 0.125;
                weight = sharp.Average(Sharpness);
            }
            else
            {
                next[v] = smooth;
                continue;
            }
            next[v] = weight >= 1 ? target : Lerp(smooth, target, weight);
        }

        var faceIndex = new int[polys.Count];
        for (var f = 0; f < polys.Count; f++)
        {
            faceIndex[f] = next.Count;
            next.Add(facePoint[f]);
        }

        // Each half of a creased edge keeps one round less of sharpness; vertices likewise.
        var childSharpness = new Dictionary<(int, int), double>();
        var childHard = new HashSet<(int, int)>();
        foreach (var ((a, b), s) in edgeSharpness)
        {
            if (!edgePoint.TryGetValue((a, b), out var m))
                continue;
            foreach (var half in new[] { Key(a, m), Key(m, b) })
            {
                if (s > 1)
                    childSharpness[half] = s - 1;
                if (hard.Contains((a, b)))
                    childHard.Add(half);
            }
        }
        var childVertex = vertexSharpness.Where(kv => kv.Value > 1).ToDictionary(kv => kv.Key, kv => kv.Value - 1);

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
        return new Level(next, quads, childSharpness, childVertex, childHard);
    }

    private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
}
