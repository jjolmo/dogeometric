using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// Curviloft (Fredo6): surfaces between curves. Loft by Spline skins two or more curves (sections following a smooth
/// spline through them); Loft along Path does it following a path curve; Skin Contours fills a loop of four curves
/// with a Coons patch.
/// </summary>
public static class Curviloft
{
    /// <summary>The selected edges as separate runs (each a chain of points), in the order found.</summary>
    public static List<List<Vec3>> Chains(IEnumerable<Edge> edges)
    {
        var set = edges.ToHashSet();
        var at = new Dictionary<Vertex, List<Edge>>();
        foreach (var e in set)
            foreach (var v in new[] { e.Start, e.End })
            {
                if (!at.TryGetValue(v, out var list))
                    at[v] = list = [];
                list.Add(e);
            }
        var chains = new List<List<Vec3>>();
        var used = new HashSet<Edge>();
        foreach (var first in set)
        {
            if (used.Contains(first))
                continue;
            // Walk back to an end (or round a loop), then forward collecting points.
            var start = first.Start;
            var came = first;
            var guard = 0;
            while (at[start].Count == 2 && guard++ < set.Count)
            {
                var other = at[start].First(x => x != came);
                if (other == first)
                    break;
                came = other;
                start = other.Other(start);
            }
            var chain = new List<Vec3> { start.Position };
            var v = start;
            // From an end where curves meet, go back along the edge that led there.
            var edge = at[start].Count != 2 && !used.Contains(came) ? came : at[start].FirstOrDefault(x => !used.Contains(x));
            while (edge != null && used.Add(edge))
            {
                v = edge.Other(v);
                chain.Add(v.Position);
                // A curve ends where it meets others (three or more edges at a point).
                edge = at[v].Count == 2 ? at[v].FirstOrDefault(x => !used.Contains(x)) : null;
            }
            chains.Add(chain);
        }
        return chains;
    }

    /// <summary>Points at <paramref name="count"/> + 1 equal steps along a chain (a closed chain ends where it starts).</summary>
    public static List<Vec3> Resample(List<Vec3> chain, int count)
    {
        var total = 0.0;
        for (var i = 1; i < chain.Count; i++)
            total += chain[i].DistanceTo(chain[i - 1]);
        var result = new List<Vec3> { chain[0] };
        var seg = 1;
        var walked = 0.0;
        for (var k = 1; k < count; k++)
        {
            var target = total * k / count;
            while (seg < chain.Count - 1 && walked + chain[seg].DistanceTo(chain[seg - 1]) < target)
            {
                walked += chain[seg].DistanceTo(chain[seg - 1]);
                seg++;
            }
            var len = chain[seg].DistanceTo(chain[seg - 1]);
            result.Add(chain[seg - 1] + (chain[seg] - chain[seg - 1]) * (len < 1e-12 ? 0 : (target - walked) / len));
        }
        result.Add(chain[^1]);
        return result;
    }

    /// <summary>
    /// Loft by Spline: curves (in order) resampled to <paramref name="segments"/> each, joined by
    /// <paramref name="between"/> rows per gap along Catmull-Rom splines through matching points. Returns faces made.
    /// </summary>
    public static int LoftBySpline(Entities e, List<List<Vec3>> curves, int segments, int between)
    {
        if (curves.Count < 2)
            return 0;
        var sections = curves.Select(c => Resample(c, segments)).ToList();
        // Curves drawn in opposite directions would twist the skin: follow the first one's direction.
        for (var i = 1; i < sections.Count; i++)
            if (sections[i][0].DistanceTo(sections[i - 1][0]) > sections[i][^1].DistanceTo(sections[i - 1][0]))
                sections[i].Reverse();
        var rows = new List<List<Vec3>>();
        for (var gap = 0; gap < sections.Count - 1; gap++)
            for (var step = 0; step < between; step++)
            {
                var t = (double)step / between;
                rows.Add(Enumerable.Range(0, segments + 1).Select(j => CatmullRom(sections, gap, j, t)).ToList());
            }
        rows.Add(sections[^1]);
        return Skin(e, rows);
    }

    private static Vec3 CatmullRom(List<List<Vec3>> s, int gap, int j, double t)
    {
        Vec3 P(int i) => i < 0 ? s[0][j] * 2 - s[1][j] : i >= s.Count ? s[^1][j] * 2 - s[^2][j] : s[i][j];
        Vec3 p0 = P(gap - 1), p1 = P(gap), p2 = P(gap + 1), p3 = P(gap + 2);
        return (p1 * 2 + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * (t * t) + (p1 * 3 - p0 - p2 * 3 + p3) * (t * t * t)) * 0.5;
    }

    /// <summary>
    /// Loft along Path: among the curves, the path is the one whose ends touch the others (the contours); the contours,
    /// ordered along it, are carried down the path in a frame turning with it (parallel transport) and blended from one
    /// to the next. Returns faces made (0 when no path touches two contours, or one contour at the path's end).
    /// </summary>
    public static int LoftAlongPath(Entities e, List<List<Vec3>> curves, int segments, int rows)
    {
        var tolerance = Tolerance.Length * 10;
        bool Touches(List<Vec3> c, Vec3 p) => c.Any(q => q.DistanceTo(p) < tolerance);
        var path = curves
            .Select(c => (Curve: c, Count: curves.Count(o => o != c && (Touches(o, c[0]) || Touches(o, c[^1])))))
            .Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenByDescending(x => Length(x.Curve)).Select(x => x.Curve).FirstOrDefault();
        if (path == null)
            return 0;
        var contours = curves.Where(c => c != path && path.Any(p => Touches(c, p))).ToList();
        if (contours.Count == 0)
            return 0;

        // Path as points with their distance along it, every contour placed where it touches it.
        var along = new List<double> { 0 };
        for (var i = 1; i < path.Count; i++)
            along.Add(along[^1] + path[i].DistanceTo(path[i - 1]));
        var placed = contours.Select(c =>
        {
            var k = Enumerable.Range(0, path.Count).MinBy(i => c.Min(q => q.DistanceTo(path[i])));
            return (Contour: c, At: along[k], Index: k);
        }).OrderBy(x => x.At).ToList();
        // One contour sweeps the whole path: the other end carries the same shape.
        var single = placed.Count == 1;
        if (single)
            placed.Add(placed[0].Index == 0 ? (placed[0].Contour, along[^1], path.Count - 1) : (placed[0].Contour, 0, 0));
        placed = placed.OrderBy(x => x.At).ToList();

        // Frames along the path: tangent and a normal carried without twisting.
        var frames = new List<(Vec3 T, Vec3 N, Vec3 B)>();
        var t0 = (path[1] - path[0]).Normalized();
        var n0 = Math.Abs(t0.Z) < 0.9 ? Vec3.UnitZ.Cross(t0).Normalized() : Vec3.UnitX.Cross(t0).Normalized();
        for (var i = 0; i < path.Count; i++)
        {
            var t = i == 0 ? t0 : i == path.Count - 1 ? (path[i] - path[i - 1]).Normalized() : (path[i + 1] - path[i - 1]).Normalized();
            if (i > 0)
            {
                var prev = frames[^1];
                n0 = (prev.N - t * prev.N.Dot(t)).Normalized();
            }
            frames.Add((t, n0, t.Cross(n0)));
        }
        (Vec3 P, (Vec3 T, Vec3 N, Vec3 B) F) At(double s)
        {
            var i = Math.Clamp(along.FindLastIndex(a => a <= s), 0, path.Count - 2);
            var u = (s - along[i]) / Math.Max(along[i + 1] - along[i], 1e-12);
            var (fa, fb) = (frames[i], frames[i + 1]);
            var tt = (fa.T * (1 - u) + fb.T * u).Normalized();
            var nn = (fa.N * (1 - u) + fb.N * u);
            nn = (nn - tt * nn.Dot(tt)).Normalized();
            return (path[i] + (path[i + 1] - path[i]) * u, (tt, nn, tt.Cross(nn)));
        }

        // Each contour in its frame's coordinates, resampled, all starting and running the same way.
        var sections = new List<List<Vec3>>();
        foreach (var (contour, at, _) in placed)
        {
            if (single && sections.Count == 1)
            {
                sections.Add([.. sections[0]]);
                continue;
            }
            var (origin, (t, n, b)) = At(at);
            var pts = Resample(contour, segments);
            var local = pts.Select(q => new Vec3((q - origin).Dot(t), (q - origin).Dot(n), (q - origin).Dot(b))).ToList();
            if (sections.Count > 0)
            {
                var prev = sections[^1];
                if (local[0].DistanceTo(prev[0]) > local[^1].DistanceTo(prev[0]))
                    local.Reverse();
            }
            sections.Add(local);
        }

        var skin = new List<List<Vec3>>();
        for (var gap = 0; gap < placed.Count - 1; gap++)
            for (var step = 0; step <= rows; step++)
            {
                if (gap > 0 && step == 0)
                    continue;
                var u = (double)step / rows;
                var (origin, (t, n, b)) = At(placed[gap].At + (placed[gap + 1].At - placed[gap].At) * u);
                skin.Add(Enumerable.Range(0, segments + 1).Select(j =>
                {
                    var l = sections[gap][j] * (1 - u) + sections[gap + 1][j] * u;
                    return origin + t * l.X + n * l.Y + b * l.Z;
                }).ToList());
            }
        return Skin(e, skin);
    }

    private static double Length(List<Vec3> c)
    {
        var total = 0.0;
        for (var i = 1; i < c.Count; i++)
            total += c[i].DistanceTo(c[i - 1]);
        return total;
    }

    /// <summary>
    /// Skin Contours: four curves meeting end to end around a loop, filled with a Coons patch of
    /// <paramref name="segments"/> by <paramref name="segments"/> cells. Returns faces made (0 if they do not close).
    /// </summary>
    public static int SkinContours(Entities e, List<List<Vec3>> curves, int segments)
    {
        if (curves.Count != 4)
            return 0;
        // Order and orient the four sides round the loop.
        var sides = new List<List<Vec3>> { curves[0] };
        var rest = curves.Skip(1).ToList();
        while (rest.Count > 0)
        {
            var end = sides[^1][^1];
            var next = rest.FirstOrDefault(c => c[0].DistanceTo(end) < Tolerance.Length * 10 || c[^1].DistanceTo(end) < Tolerance.Length * 10);
            if (next == null)
                return 0;
            rest.Remove(next);
            sides.Add(next[0].DistanceTo(end) < Tolerance.Length * 10 ? next : Enumerable.Reverse(next).ToList());
        }
        if (sides[^1][^1].DistanceTo(sides[0][0]) > Tolerance.Length * 10)
            return 0;
        var bottom = Resample(sides[0], segments);
        var right = Resample(sides[1], segments);
        var top = Resample(sides[2], segments);
        top.Reverse();
        var left = Resample(sides[3], segments);
        left.Reverse();
        var rows = new List<List<Vec3>>();
        for (var i = 0; i <= segments; i++)
        {
            var v = (double)i / segments;
            var row = new List<Vec3>();
            for (var j = 0; j <= segments; j++)
            {
                var u = (double)j / segments;
                var ruled = bottom[j] * (1 - v) + top[j] * v + left[i] * (1 - u) + right[i] * u;
                var bilinear = bottom[0] * ((1 - u) * (1 - v)) + bottom[^1] * (u * (1 - v)) + top[0] * ((1 - u) * v) + top[^1] * (u * v);
                row.Add(ruled - bilinear);
            }
            rows.Add(row);
        }
        return Skin(e, rows);
    }

    /// <summary>Faces between consecutive rows of matching points (quads, or two triangles where not flat), soft inside.</summary>
    private static int Skin(Entities e, List<List<Vec3>> rows)
    {
        var weld = new Welder(e);
        var made = new List<Face>();
        for (var r = 0; r + 1 < rows.Count; r++)
            for (var j = 0; j + 1 < rows[r].Count; j++)
            {
                List<Vec3> quad = [rows[r][j], rows[r][j + 1], rows[r + 1][j + 1], rows[r + 1][j]];
                var distinct = quad.Distinct().ToList();
                if (distinct.Count < 3)
                    continue;
                var n = Polygon.Normal(distinct);
                var flat = distinct.All(p => Math.Abs((p - distinct[0]).Dot(n)) < Tolerance.Length);
                if (flat)
                    made.Add(weld.Face(distinct, []));
                else
                {
                    made.Add(weld.Face([quad[0], quad[1], quad[2]], []));
                    made.Add(weld.Face([quad[0], quad[2], quad[3]], []));
                }
            }
        // Fronts face outwards: away from the middle of all the points, on the whole.
        var middle = rows.SelectMany(r => r).Aggregate(Vec3.Zero, (acc, p) => acc + p) / rows.Sum(r => r.Count);
        var outward = made.Sum(f => f.Normal.Dot(f.OuterLoop.Points.Aggregate(Vec3.Zero, (acc, p) => acc + p) / f.OuterLoop.Edges.Count - middle));
        if (outward < 0)
            foreach (var f in made)
                FaceFinder.Reverse(f);
        foreach (var edge in made.SelectMany(Topology.EdgesOf).Distinct())
            if (Topology.FacesOf(e, edge).Count() == 2)
                edge.Flags |= EdgeFlags.Soft | EdgeFlags.Smooth;
        return made.Count;
    }
}
