using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// Curviloft (Fredo6): surfaces between curves. Loft by Spline skins two or more curves (sections following a smooth
/// spline through them); Skin Contours fills a loop of four curves with a Coons patch.
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
            var edge = at[start].FirstOrDefault(x => !used.Contains(x));
            while (edge != null && used.Add(edge))
            {
                v = edge.Other(v);
                chain.Add(v.Position);
                edge = at[v].FirstOrDefault(x => !used.Contains(x));
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
