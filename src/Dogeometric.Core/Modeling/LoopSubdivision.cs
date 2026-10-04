using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>"Loop subdivision smooth" (Nathan B): triangulates and splits each triangle in four with Charles Loop's weights;
/// the outline stays put and neighbouring faces get the same splits, so the mesh stays closed.</summary>
public static class LoopSubdivision
{
    /// <summary>Subdivides <paramref name="faces"/> of <paramref name="e"/> <paramref name="repeats"/> times; returns the triangles made.</summary>
    public static int Apply(Entities e, IEnumerable<Face> faces, int repeats, bool soften)
    {
        var set = faces.Where(e.Faces.Contains).ToHashSet();
        if (set.Count == 0)
            return 0;

        var points = new List<Vec3>();
        var index = new Dictionary<Vertex, int>();
        int Index(Vertex v)
        {
            if (!index.TryGetValue(v, out var i))
            {
                index[v] = i = points.Count;
                points.Add(v.Position);
            }
            return i;
        }
        var triangles = new List<(int A, int B, int C, Face Source)>();
        foreach (var f in set)
            Triangulate(f, Index, points, triangles);

        // Edges along the outline of the faces, shared with faces left alone: they get the same splits.
        var outline = new List<(Edge Edge, List<int> Chain)>();
        foreach (var edge in set.SelectMany(Topology.EdgesOf).Distinct())
            if (Topology.FacesOf(e, edge).Any(f => !set.Contains(f)))
                outline.Add((edge, [Index(edge.Start), Index(edge.End)]));

        for (var round = 0; round < repeats; round++)
        {
            var opposite = new Dictionary<(int, int), List<int>>();
            void Note(int a, int b, int c)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!opposite.TryGetValue(key, out var list))
                    opposite[key] = list = [];
                list.Add(c);
            }
            foreach (var (a, b, c, _) in triangles)
            {
                Note(a, b, c);
                Note(b, c, a);
                Note(c, a, b);
            }

            var neighbours = new Dictionary<int, HashSet<int>>();
            var fixedPoints = new HashSet<int>();
            foreach (var ((a, b), opp) in opposite)
            {
                foreach (var (x, y) in new[] { (a, b), (b, a) })
                {
                    if (!neighbours.TryGetValue(x, out var set2))
                        neighbours[x] = set2 = [];
                    set2.Add(y);
                }
                if (opp.Count != 2)
                {
                    fixedPoints.Add(a);
                    fixedPoints.Add(b);
                }
            }

            var next = new List<Vec3>(points);
            foreach (var (v, around) in neighbours)
            {
                if (fixedPoints.Contains(v))
                    continue;
                var n = around.Count;
                var beta = n == 3 ? 3.0 / 16 : 3.0 / (8 * n);
                var sum = around.Aggregate(Vec3.Zero, (acc, i) => acc + points[i]);
                next[v] = points[v] * (1 - n * beta) + sum * beta;
            }
            var mid = new Dictionary<(int, int), int>();
            foreach (var ((a, b), opp) in opposite)
            {
                var p = opp.Count == 2
                    ? (points[a] + points[b]) * (3.0 / 8) + (points[opp[0]] + points[opp[1]]) * (1.0 / 8)
                    : (points[a] + points[b]) / 2;
                mid[(a, b)] = next.Count;
                next.Add(p);
            }
            int M(int a, int b) => mid[a < b ? (a, b) : (b, a)];

            var split = new List<(int, int, int, Face)>();
            foreach (var (a, b, c, src) in triangles)
            {
                int ab = M(a, b), bc = M(b, c), ca = M(c, a);
                split.Add((a, ab, ca, src));
                split.Add((b, bc, ab, src));
                split.Add((c, ca, bc, src));
                split.Add((ab, bc, ca, src));
            }
            triangles = split;
            points = next;
            foreach (var (_, chain) in outline)
            {
                var longer = new List<int>();
                for (var i = 0; i < chain.Count - 1; i++)
                {
                    longer.Add(chain[i]);
                    longer.Add(M(chain[i], chain[i + 1]));
                }
                longer.Add(chain[^1]);
                chain.Clear();
                chain.AddRange(longer);
            }
        }

        // Out with the old faces and their own edges; the outline edges are replaced by their split runs.
        var outlineEdges = outline.Select(o => o.Edge).ToHashSet();
        var own = set.SelectMany(Topology.EdgesOf).Where(x => !outlineEdges.Contains(x)).ToHashSet();
        e.Faces.RemoveAll(set.Contains);
        e.Edges.RemoveAll(own.Contains);
        var weld = new Welder(e);
        foreach (var (edge, chain) in outline)
        {
            var runForward = new List<Edge>();
            for (var i = 0; i < chain.Count - 1; i++)
                runForward.Add(weld.EdgeBetween(weld.VertexAt(points[chain[i]]), weld.VertexAt(points[chain[i + 1]])));
            foreach (var f in e.Faces)
                foreach (var loop in f.Loops)
                {
                    var at = loop.Edges.FindIndex(x => x.Edge == edge);
                    if (at < 0)
                        continue;
                    var reversed = loop.Edges[at].Reversed;
                    var run = reversed ? Enumerable.Reverse(runForward).ToList() : runForward;
                    var from = reversed ? edge.End : edge.Start;
                    var entries = new List<(Edge, bool)>();
                    var current = weld.VertexAt(from.Position);
                    foreach (var piece in run)
                    {
                        entries.Add((piece, piece.Start != current));
                        current = piece.Other(current);
                    }
                    loop.Edges.RemoveAt(at);
                    loop.Edges.InsertRange(at, entries);
                }
            e.Edges.Remove(edge);
        }

        var made = new HashSet<Face>();
        foreach (var (a, b, c, src) in triangles)
        {
            var f = weld.Face([points[a], points[b], points[c]], []);
            f.FrontMaterial = src.FrontMaterial;
            f.BackMaterial = src.BackMaterial;
            f.Tag = src.Tag;
            made.Add(f);
        }
        if (soften)
            foreach (var edge in made.SelectMany(Topology.EdgesOf).Distinct())
                if (Topology.FacesOf(e, edge).All(made.Contains))
                    edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        Editing.RemoveOrphanVertices(e);
        return triangles.Count;
    }

    /// <summary>Convex faces fan out from their centroid, like the script; others are ear-clipped.</summary>
    private static void Triangulate(Face f, Func<Vertex, int> index, List<Vec3> points, List<(int, int, int, Face)> triangles)
    {
        var outer = f.OuterLoop.Vertices.ToList();
        var normal = f.Normal.Normalized();
        if (f.Loops.Count == 1 && outer.Count == 3)
        {
            triangles.Add((index(outer[0]), index(outer[1]), index(outer[2]), f));
            return;
        }
        if (f.Loops.Count == 1 && Convex(outer.Select(v => v.Position).ToList(), normal))
        {
            var centre = points.Count;
            points.Add(outer.Aggregate(Vec3.Zero, (a, v) => a + v.Position) / outer.Count);
            for (var i = 0; i < outer.Count; i++)
                triangles.Add((index(outer[i]), index(outer[(i + 1) % outer.Count]), centre, f));
            return;
        }
        var loops = f.Loops.Select(l => l.Vertices.ToList()).ToList();
        var flat = loops.SelectMany(l => l).ToList();
        var tri = Polygon.Triangulate(loops[0].Select(v => v.Position).ToList(),
            loops.Skip(1).Select(l => (IReadOnlyList<Vec3>)l.Select(v => v.Position).ToList()).ToList());
        for (var i = 0; i + 2 < tri.Count; i += 3)
        {
            var (a, b, c) = (flat[tri[i]], flat[tri[i + 1]], flat[tri[i + 2]]);
            if ((b.Position - a.Position).Cross(c.Position - a.Position).Dot(normal) < 0)
                (b, c) = (c, b);
            triangles.Add((index(a), index(b), index(c), f));
        }
    }

    private static bool Convex(List<Vec3> pts, Vec3 normal)
    {
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var c = pts[(i + 2) % pts.Count];
            if ((b - a).Cross(c - b).Dot(normal) < -1e-9)
                return false;
        }
        return true;
    }
}
