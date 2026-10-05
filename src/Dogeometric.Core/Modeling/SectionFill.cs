using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>SketchUp's Section Fill: the closed loops of the active section cut, filled (holes left open), in world space.</summary>
public static class SectionFill
{
    /// <summary>Triangles (three points each) covering the cut, or empty without an active section.</summary>
    public static List<Vec3> Triangles(Model model) =>
        model.Entities.ActiveSection is { } plane ? Triangles(Intersect.SectionCut(model), plane.Normal) : [];

    public static List<Vec3> Triangles(IReadOnlyList<(Vec3 A, Vec3 B)> segments, Vec3 normal)
    {
        var loops = Loops(segments);
        var (u, v) = Polygon.PlaneAxes(normal);
        var flat = loops.Select(l => l.Select(p => (p.Dot(u), p.Dot(v))).ToArray()).ToList();

        // Even nesting depth: an outline; odd: a hole in the smallest outline around it.
        var depth = new int[loops.Count];
        for (var i = 0; i < loops.Count; i++)
            for (var j = 0; j < loops.Count; j++)
                if (i != j && Inside(flat[i][0], flat[j]) && Math.Abs(Area(flat[j])) > Math.Abs(Area(flat[i])))
                    depth[i]++;
        var result = new List<Vec3>();
        for (var o = 0; o < loops.Count; o++)
        {
            if (depth[o] % 2 != 0)
                continue;
            var holes = Enumerable.Range(0, loops.Count)
                .Where(h => depth[h] == depth[o] + 1 && Inside(flat[h][0], flat[o]))
                .Select(h => (IReadOnlyList<Vec3>)loops[h]).ToList();
            var outer = loops[o];
            if (Polygon.Normal(outer).Dot(normal) < 0)
                outer = Enumerable.Reverse(outer).ToList();
            var all = outer.Concat(holes.SelectMany(h => h)).ToList();
            result.AddRange(Polygon.Triangulate(outer, holes).Select(i => all[i]));
        }
        return result;
    }

    /// <summary>
    /// Section plane › Troubleshoot Section Fill: where the cut doesn't close, so it can't be filled: points where a
    /// run of the cut stops (an odd number of its segments meet there), as left by holes or gaps in the geometry.
    /// </summary>
    public static List<Vec3> Problems(IReadOnlyList<(Vec3 A, Vec3 B)> segments)
    {
        var cell = Tolerance.Length * 10;
        var ends = new List<(Vec3 Point, int Count)>();
        void Touch(Vec3 p)
        {
            var i = ends.FindIndex(e => e.Point.DistanceTo(p) < cell);
            if (i < 0)
                ends.Add((p, 1));
            else
                ends[i] = (ends[i].Point, ends[i].Count + 1);
        }
        foreach (var (a, b) in segments)
        {
            if (a.DistanceTo(b) < cell)
                continue;
            Touch(a);
            Touch(b);
        }
        return ends.Where(e => e.Count % 2 == 1).Select(e => e.Point).ToList();
    }

    /// <summary>The segments joined end to end into closed loops; open runs are dropped.</summary>
    public static List<List<Vec3>> Loops(IReadOnlyList<(Vec3 A, Vec3 B)> segments)
    {
        // Ends closer than the tolerance are one point.
        var points = new List<Vec3>();
        var grid = new Dictionary<(long, long, long), List<int>>();
        var cell = Tolerance.Length * 10;
        int Id(Vec3 p)
        {
            var key = ((long)Math.Floor(p.X / cell), (long)Math.Floor(p.Y / cell), (long)Math.Floor(p.Z / cell));
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    for (var dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((key.Item1 + dx, key.Item2 + dy, key.Item3 + dz), out var near))
                            foreach (var i in near)
                                if (points[i].DistanceTo(p) < cell)
                                    return i;
            points.Add(p);
            if (!grid.TryGetValue(key, out var list))
                grid[key] = list = [];
            list.Add(points.Count - 1);
            return points.Count - 1;
        }

        var links = new Dictionary<int, List<int>>();
        var seen = new HashSet<(int, int)>();
        foreach (var (a, b) in segments)
        {
            int i = Id(a), j = Id(b);
            // Coincident segments (faces of two touching solids) would make a two-point loop.
            if (i == j || !seen.Add((Math.Min(i, j), Math.Max(i, j))))
                continue;
            foreach (var (from, to) in new[] { (i, j), (j, i) })
            {
                if (!links.TryGetValue(from, out var list))
                    links[from] = list = [];
                list.Add(to);
            }
        }

        var used = new HashSet<(int, int)>();
        var loops = new List<List<Vec3>>();
        foreach (var start in links.Keys)
            foreach (var first in links[start])
            {
                if (used.Contains((Math.Min(start, first), Math.Max(start, first))))
                    continue;
                var loop = new List<int> { start };
                var (prev, here) = (start, first);
                used.Add((Math.Min(start, first), Math.Max(start, first)));
                while (here != start)
                {
                    loop.Add(here);
                    var next = links[here].FirstOrDefault(n => n != prev && !used.Contains((Math.Min(here, n), Math.Max(here, n))), -1);
                    if (next < 0)
                        break;
                    used.Add((Math.Min(here, next), Math.Max(here, next)));
                    (prev, here) = (here, next);
                }
                if (here == start && loop.Count >= 3)
                    loops.Add(loop.Select(i => points[i]).ToList());
            }
        return loops;
    }

    private static double Area((double X, double Y)[] p)
    {
        double a = 0;
        for (var i = 0; i < p.Length; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Length];
            a += x1 * y2 - x2 * y1;
        }
        return a / 2;
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
