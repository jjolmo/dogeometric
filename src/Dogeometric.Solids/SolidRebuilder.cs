using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Solids;

/// <summary>
/// Turns a boolean result (triangles) back into SketchUp-style geometry: connected coplanar triangles become one
/// face (with holes), sharing edges and vertices; each face takes the material of an input face in its plane.
/// </summary>
public static class SolidRebuilder
{
    private const double PlaneTolerance = 1e-3;

    public sealed record PlaneMaterial(Vec3 Normal, double D, Material? Front, Material? Back, Tag? Tag);

    public static void Build(Entities target, List<Vec3> positions, List<int> tris, IReadOnlyList<PlaneMaterial> sources)
    {
        var verts = positions.Select(target.AddVertex).ToArray();
        var count = tris.Count / 3;
        var normals = new Vec3[count];
        for (var i = 0; i < count; i++)
            normals[i] = (positions[tris[i * 3 + 1]] - positions[tris[i * 3]]).Cross(positions[tris[i * 3 + 2]] - positions[tris[i * 3]]).Normalized();

        // Triangle adjacency through shared (undirected) edges.
        var byEdge = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < count; i++)
        {
            for (var k = 0; k < 3; k++)
            {
                var a = tris[i * 3 + k];
                var b = tris[i * 3 + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                if (!byEdge.TryGetValue(key, out var list))
                    byEdge[key] = list = [];
                list.Add(i);
            }
        }

        // Flood-fill coplanar regions.
        var region = Enumerable.Repeat(-1, count).ToArray();
        var regions = new List<List<int>>();
        for (var seed = 0; seed < count; seed++)
        {
            if (region[seed] >= 0 || normals[seed].IsZero(1e-12))
                continue;
            var id = regions.Count;
            var members = new List<int>();
            var n = normals[seed];
            var d = n.Dot(positions[tris[seed * 3]]);
            var stack = new Stack<int>([seed]);
            region[seed] = id;
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                members.Add(t);
                for (var k = 0; k < 3; k++)
                {
                    var a = tris[t * 3 + k];
                    var b = tris[t * 3 + (k + 1) % 3];
                    foreach (var o in byEdge[a < b ? (a, b) : (b, a)])
                    {
                        if (region[o] >= 0 || normals[o].Dot(n) < 1 - 1e-6)
                            continue;
                        if (Enumerable.Range(0, 3).Any(j => Math.Abs(n.Dot(positions[tris[o * 3 + j]]) - d) > PlaneTolerance))
                            continue;
                        region[o] = id;
                        stack.Push(o);
                    }
                }
            }
            regions.Add(members);
        }

        // Boundary loops of every region first, so collinear vertices are only dropped where every face that
        // uses them agrees (dropping a T-junction corner from one side would open a crack).
        var regionLoops = regions.Select(members => BoundaryLoops(members, tris)).ToList();
        var removable = new Dictionary<int, bool>();
        foreach (var loops in regionLoops)
        {
            foreach (var loop in loops)
            {
                for (var i = 0; i < loop.Count; i++)
                {
                    var collinear = IsCollinear(positions[loop[(i - 1 + loop.Count) % loop.Count]], positions[loop[i]], positions[loop[(i + 1) % loop.Count]]);
                    removable[loop[i]] = removable.GetValueOrDefault(loop[i], true) && collinear;
                }
            }
        }

        for (var r = 0; r < regions.Count; r++)
        {
            var members = regions[r];
            var loops = regionLoops[r].Select(l => l.Where(v => !removable[v]).ToList()).Where(l => l.Count >= 3).ToList();
            if (loops.Count == 0)
                continue;

            var normal = normals[members[0]];
            // The outer loop has the largest area; it winds counter-clockwise around the outward normal already.
            loops.Sort((x, y) => Polygon.Area(y.Select(i => positions[i]).ToList()).CompareTo(Polygon.Area(x.Select(i => positions[i]).ToList())));
            var face = new Face();
            foreach (var l in loops)
            {
                var loop = new FaceLoop();
                for (var i = 0; i < l.Count; i++)
                {
                    var va = verts[l[i]];
                    var vb = verts[l[(i + 1) % l.Count]];
                    var e = target.EdgeBetween(va, vb);
                    loop.Edges.Add((e, e.Start != va));
                }
                face.Loops.Add(loop);
            }
            var plane = normal.Dot(positions[loops[0][0]]);
            var src = sources.FirstOrDefault(s => s.Normal.Dot(normal) > 1 - 1e-6 && Math.Abs(s.D - plane) <= PlaneTolerance);
            face.FrontMaterial = src?.Front;
            face.BackMaterial = src?.Back;
            face.Tag = src?.Tag;
            target.Faces.Add(face);
        }
        Editing.RemoveOrphanVertices(target);
    }

    /// <summary>Boundary loops of a set of triangles: directed edges whose reverse is not in the set, chained.</summary>
    private static List<List<int>> BoundaryLoops(List<int> members, List<int> tris)
    {
        var directed = new HashSet<(int, int)>();
        foreach (var t in members)
            for (var k = 0; k < 3; k++)
                directed.Add((tris[t * 3 + k], tris[t * 3 + (k + 1) % 3]));
        var boundary = directed.Where(e => !directed.Contains((e.Item2, e.Item1))).ToList();
        var next = new Dictionary<int, List<int>>();
        foreach (var (a, b) in boundary)
        {
            if (!next.TryGetValue(a, out var l))
                next[a] = l = [];
            l.Add(b);
        }
        var loops = new List<List<int>>();
        var used = new HashSet<(int, int)>();
        foreach (var (a0, b0) in boundary)
        {
            if (used.Contains((a0, b0)))
                continue;
            var loop = new List<int> { a0 };
            var (a, b) = (a0, b0);
            while (used.Add((a, b)))
            {
                if (b == a0)
                    break;
                loop.Add(b);
                var options = next[b].Where(c => !used.Contains((b, c))).ToList();
                if (options.Count == 0)
                    break;
                (a, b) = (b, options[0]);
            }
            if (loop.Count >= 3)
                loops.Add(loop);
        }
        return loops;
    }

    private static bool IsCollinear(Vec3 a, Vec3 b, Vec3 c) =>
        (b - a).Cross(c - b).Length <= 1e-9 * Math.Max(1, (c - a).LengthSquared);

}
