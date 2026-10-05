using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's auto-fold: when Move, Rotate or Scale take some of a face's vertices off its plane, the face splits
/// into flat pieces along diagonals of its old shape (the part left in place stays one face), so faces stay planar.
/// </summary>
public static class AutoFold
{
    public sealed record Bendable(Face Face, Vertex[][] Triangles);

    /// <summary>The faces that may bend when <paramref name="moving"/> move, each with its triangles (wound like it).</summary>
    public static List<Bendable> Prepare(Entities e, IReadOnlySet<Vertex> moving)
    {
        var result = new List<Bendable>();
        foreach (var face in e.Faces)
        {
            var loops = face.Loops.Select(l => l.Vertices.ToList()).ToList();
            var count = loops.Sum(l => l.Count(moving.Contains));
            if (count == 0 || count == loops.Sum(l => l.Count))
                continue;
            var all = loops.SelectMany(l => l).ToList();
            var indices = Polygon.Triangulate(loops[0].Select(v => v.Position).ToList(),
                loops.Skip(1).Select(l => (IReadOnlyList<Vec3>)l.Select(v => v.Position).ToList()).ToList());
            var triangles = Enumerable.Range(0, indices.Count / 3).Select(t => new[] { all[indices[3 * t]], all[indices[3 * t + 1]], all[indices[3 * t + 2]] }).ToArray();
            result.Add(new Bendable(face, triangles));
        }
        return result;
    }

    /// <summary>Splits the prepared faces that are no longer flat into flat pieces. Returns the new faces.</summary>
    public static List<Face> Fold(Entities e, IEnumerable<Bendable> prepared)
    {
        var made = new List<Face>();
        foreach (var (face, all) in prepared)
        {
            if (!e.Faces.Contains(face) || IsFlat(face))
                continue;
            var triangles = all.Where(t => (t[1].Position - t[0].Position).Cross(t[2].Position - t[0].Position).Length > Tolerance.Length * Tolerance.Length).ToList();
            var normals = triangles.Select(t => (t[1].Position - t[0].Position).Cross(t[2].Position - t[0].Position).Normalized()).ToList();

            // Neighbouring triangles still in one plane make one piece.
            var parent = Enumerable.Range(0, triangles.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            var byEdge = new Dictionary<(Vertex, Vertex), int>();
            for (var t = 0; t < triangles.Count; t++)
                for (var k = 0; k < 3; k++)
                {
                    var (a, b) = (triangles[t][k], triangles[t][(k + 1) % 3]);
                    if (byEdge.TryGetValue((b, a), out var other) && Coplanar(triangles[t], normals[t], triangles[other], normals[other]))
                        parent[Find(t)] = Find(other);
                    byEdge[(a, b)] = t;
                }

            e.Faces.Remove(face);
            foreach (var piece in Enumerable.Range(0, triangles.Count).GroupBy(Find))
                made.AddRange(Pieces(e, face, piece.Select(t => triangles[t]).ToList(), normals[piece.Key]));
        }
        return made;
    }

    private static bool IsFlat(Face f)
    {
        var n = f.Normal;
        var p = f.OuterLoop.Points.First();
        return f.Loops.SelectMany(l => l.Points).All(q => Math.Abs((q - p).Dot(n)) <= Tolerance.Length);
    }

    private static bool Coplanar(Vertex[] a, Vec3 na, Vertex[] b, Vec3 nb) =>
        na.Dot(nb) > 1 - 1e-9 && b.All(v => Math.Abs((v.Position - a[0].Position).Dot(na)) <= Tolerance.Length);

    /// <summary>The faces outlining a set of triangles: its boundary loops, the one facing like the triangles outside.</summary>
    private static IEnumerable<Face> Pieces(Entities e, Face source, List<Vertex[]> triangles, Vec3 normal)
    {
        var directed = new HashSet<(Vertex, Vertex)>();
        foreach (var t in triangles)
            for (var k = 0; k < 3; k++)
            {
                var (a, b) = (t[k], t[(k + 1) % 3]);
                if (!directed.Remove((b, a)))
                    directed.Add((a, b));
            }
        var next = directed.GroupBy(d => d.Item1).ToDictionary(g => g.Key, g => new Queue<Vertex>(g.Select(d => d.Item2)));
        var loops = new List<List<Vertex>>();
        foreach (var start in next.Keys.ToList())
            while (next[start].Count > 0)
            {
                var loop = new List<Vertex>();
                var at = start;
                do
                {
                    loop.Add(at);
                    at = next[at].Dequeue();
                }
                while (at != start && next.TryGetValue(at, out var q) && q.Count > 0 && loop.Count <= directed.Count);
                if (at == start && loop.Count >= 3)
                    loops.Add(loop);
            }
        var outers = loops.Where(l => Polygon.Normal(l.Select(v => v.Position).ToList()).Dot(normal) > 0).ToList();
        var holes = loops.Except(outers).ToList();
        foreach (var outer in outers.OrderByDescending(l => Polygon.Area(l.Select(v => v.Position).ToList())))
        {
            var face = new Face
            {
                FrontMaterial = source.FrontMaterial, BackMaterial = source.BackMaterial, Tag = source.Tag, Hidden = source.Hidden,
                FrontMapping = source.FrontMapping, BackMapping = source.BackMapping,
            };
            face.Loops.Add(Loop(e, outer));
            // Holes go to the first (largest) outline; pieces rarely have more than one.
            foreach (var h in holes)
                face.Loops.Add(Loop(e, h));
            holes.Clear();
            e.Faces.Add(face);
            yield return face;
        }
    }

    private static FaceLoop Loop(Entities e, List<Vertex> vertices)
    {
        var loop = new FaceLoop();
        for (var i = 0; i < vertices.Count; i++)
        {
            var (a, b) = (vertices[i], vertices[(i + 1) % vertices.Count]);
            var edge = e.EdgeBetween(a, b);
            loop.Edges.Add((edge, edge.Start != a));
        }
        return loop;
    }
}
