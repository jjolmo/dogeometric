using System.Globalization;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// File › Import of STL (binary or ASCII) and OBJ meshes: one group named after the file, its triangles welded into
/// shared edges and coplanar neighbours merged into single faces, as SketchUp's STL importer does by default.
/// </summary>
public static class MeshImport
{
    /// <param name="mmPerUnit">The file's unit in millimetres (STL and OBJ carry none; 1 for millimetres).</param>
    public static Model Load(string path, double mmPerUnit = 1)
    {
        var data = File.ReadAllBytes(path);
        var polygons = Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase)
            ? ReadObj(Encoding.UTF8.GetString(data))
            : ReadStl(data);
        return Build(Path.GetFileNameWithoutExtension(path), polygons, mmPerUnit);
    }

    /// <summary>The triangles of an STL file: binary when its size matches its triangle count, ASCII otherwise.</summary>
    public static List<List<Vec3>> ReadStl(byte[] data)
    {
        var result = new List<List<Vec3>>();
        if (data.Length >= 84 && 84 + 50L * BitConverter.ToUInt32(data, 80) == data.Length)
        {
            var count = BitConverter.ToUInt32(data, 80);
            for (var i = 0; i < count; i++)
            {
                var at = 84 + i * 50 + 12;
                Vec3 V(int k) => new(BitConverter.ToSingle(data, at + k * 12), BitConverter.ToSingle(data, at + k * 12 + 4), BitConverter.ToSingle(data, at + k * 12 + 8));
                result.Add([V(0), V(1), V(2)]);
            }
            return result;
        }
        var current = new List<Vec3>();
        foreach (var raw in Encoding.ASCII.GetString(data).Split('\n'))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[0] == "vertex")
                current.Add(new Vec3(Num(parts[1]), Num(parts[2]), Num(parts[3])));
            else if (parts.Length > 0 && parts[0] == "endloop")
            {
                if (current.Count >= 3)
                    result.Add(current);
                current = [];
            }
        }
        return result;
    }

    /// <summary>The faces of an OBJ file (vertices "v", faces "f" with 1-based or negative indices).</summary>
    public static List<List<Vec3>> ReadObj(string text)
    {
        var vertices = new List<Vec3>();
        var result = new List<List<Vec3>>();
        foreach (var raw in text.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && parts[0] == "v")
                vertices.Add(new Vec3(Num(parts[1]), Num(parts[2]), Num(parts[3])));
            else if (parts.Length >= 4 && parts[0] == "f")
            {
                var face = new List<Vec3>();
                foreach (var p in parts.Skip(1))
                {
                    var index = int.Parse(p.Split('/')[0], CultureInfo.InvariantCulture);
                    var i = index < 0 ? vertices.Count + index : index - 1;
                    if (i >= 0 && i < vertices.Count)
                        face.Add(vertices[i]);
                }
                if (face.Count >= 3)
                    result.Add(face);
            }
        }
        return result;
    }

    private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>A model holding the polygons as one group; <paramref name="materials"/> paints each polygon (by index).</summary>
    public static Model Build(string name, IEnumerable<List<Vec3>> polygons, double mmPerUnit = 1, IReadOnlyList<Material?>? materials = null)
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = name, IsGroup = true };

        // Corners welded by position, polygons cut into triangles (fans; files hold convex faces).
        var points = new List<Vec3>();
        var grid = new Dictionary<(long, long, long), List<int>>();
        var cell = Tolerance.Length * 4;
        int Id(Vec3 p)
        {
            var key = ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));
            for (var x = key.Item1 - 1; x <= key.Item1 + 1; x++)
                for (var y = key.Item2 - 1; y <= key.Item2 + 1; y++)
                    for (var z = key.Item3 - 1; z <= key.Item3 + 1; z++)
                        if (grid.TryGetValue((x, y, z), out var near))
                            foreach (var i in near)
                                if (points[i].DistanceTo(p) <= Tolerance.Length)
                                    return i;
            points.Add(p);
            if (!grid.TryGetValue(key, out var list))
                grid[key] = list = [];
            list.Add(points.Count - 1);
            return points.Count - 1;
        }
        var triangles = new List<(int A, int B, int C, Vec3 Normal)>();
        var painted = new List<Material?>();
        var index = -1;
        foreach (var polygon in polygons)
        {
            index++;
            var material = materials != null && index < materials.Count ? materials[index] : null;
            var ids = polygon.Select(p => Id(p * mmPerUnit)).ToList();
            for (var i = 1; i + 1 < ids.Count; i++)
            {
                var (a, b, c) = (ids[0], ids[i], ids[i + 1]);
                var n = (points[b] - points[a]).Cross(points[c] - points[a]);
                if (a == b || b == c || a == c || n.Length < Tolerance.Length * Tolerance.Length)
                    continue;
                triangles.Add((a, b, c, n.Normalized()));
                painted.Add(material);
            }
        }

        // A triangle repeated with the opposite winding is the back of the first (COLLADA writes back sides apart).
        var backs = new Material?[triangles.Count];
        var seen = new Dictionary<(int, int, int), int>();
        var kept = new List<int>();
        for (var t = 0; t < triangles.Count; t++)
        {
            var (a, b, c, _) = triangles[t];
            var corners = new[] { a, b, c };
            Array.Sort(corners);
            if (seen.TryGetValue((corners[0], corners[1], corners[2]), out var first))
            {
                if (triangles[first].Normal.Dot(triangles[t].Normal) < 0)
                    backs[first] ??= painted[t];
                continue;
            }
            seen[(corners[0], corners[1], corners[2])] = t;
            kept.Add(t);
        }
        triangles = kept.Select(t => triangles[t]).ToList();
        var backPainted = kept.Select(t => backs[t]).ToList();
        painted = kept.Select(t => painted[t]).ToList();

        // Neighbouring triangles in the same plane, facing the same way, make one face.
        var parent = Enumerable.Range(0, triangles.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        var byEdge = new Dictionary<(int, int), List<int>>();
        for (var t = 0; t < triangles.Count; t++)
        {
            var (a, b, c, _) = triangles[t];
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = u < v ? (u, v) : (v, u);
                if (!byEdge.TryGetValue(key, out var list))
                    byEdge[key] = list = [];
                list.Add(t);
            }
        }
        foreach (var list in byEdge.Values.Where(l => l.Count == 2))
        {
            var (t1, t2) = (triangles[list[0]], triangles[list[1]]);
            if (t1.Normal.Dot(t2.Normal) > 1 - 1e-9 && Math.Abs((points[t2.A] - points[t1.A]).Dot(t1.Normal)) < Tolerance.Length
                && painted[list[0]] == painted[list[1]] && backPainted[list[0]] == backPainted[list[1]])
                parent[Find(list[0])] = Find(list[1]);
        }

        var welder = new Welder(def.Entities);
        foreach (var cluster in Enumerable.Range(0, triangles.Count).GroupBy(Find))
        {
            // The cluster's outline: directed edges whose reverse is not in the cluster, chained into loops.
            var directed = new HashSet<(int, int)>();
            foreach (var t in cluster)
            {
                var (a, b, c, _) = triangles[t];
                foreach (var e in new[] { (a, b), (b, c), (c, a) })
                    if (!directed.Remove((e.Item2, e.Item1)))
                        directed.Add(e);
            }
            var next = new Dictionary<int, Queue<int>>();
            foreach (var (u, v) in directed)
            {
                if (!next.TryGetValue(u, out var q))
                    next[u] = q = new Queue<int>();
                q.Enqueue(v);
            }
            var normal = triangles[cluster.First()].Normal;
            var loops = new List<List<Vec3>>();
            foreach (var start in next.Keys.ToList())
                while (next[start].Count > 0)
                {
                    var loop = new List<Vec3>();
                    var at = start;
                    do
                    {
                        loop.Add(points[at]);
                        at = next[at].Dequeue();
                    }
                    while (at != start && next.ContainsKey(at) && next[at].Count > 0 && loop.Count <= directed.Count);
                    if (at == start && loop.Count >= 3)
                        loops.Add(loop);
                }
            var outers = loops.Where(l => Polygon.Normal(l).Dot(normal) > 0).ToList();
            var holes = loops.Except(outers).ToList();
            var (u2, v2) = Polygon.PlaneAxes(normal);
            (double X, double Y)[] Flat(List<Vec3> l) => l.Select(p => (p.Dot(u2), p.Dot(v2))).ToArray();
            foreach (var outer in outers)
            {
                var flat = Flat(outer);
                var mine = holes.Where(h => Inside(Flat(h)[0], flat)).ToList();
                holes = holes.Except(mine).ToList();
                var face = welder.Face(outer, mine);
                (face.FrontMaterial, face.BackMaterial) = (painted[cluster.First()], backPainted[cluster.First()]);
            }
        }
        foreach (var m in painted.Concat(backPainted).OfType<Material>().Distinct())
            model.Materials.Add(m);
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        return model;
    }

    private static bool Inside((double X, double Y) q, (double X, double Y)[] poly)
    {
        var inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > q.Y) != (poly[j].Y > q.Y) &&
                q.X < (poly[j].X - poly[i].X) * (q.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }
}
