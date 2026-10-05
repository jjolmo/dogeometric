namespace Dogeometric.Core.Geometry;

/// <summary>Planar polygon helpers: normal, area and triangulation (with holes).</summary>
public static class Polygon
{
    /// <summary>Unit normal by Newell's method; follows the right-hand rule of the winding.</summary>
    public static Vec3 Normal(IReadOnlyList<Vec3> pts) => NewellVector(pts).Normalized();

    public static double Area(IReadOnlyList<Vec3> pts) => NewellVector(pts).Length * 0.5;

    private static Vec3 NewellVector(IReadOnlyList<Vec3> pts)
    {
        double x = 0, y = 0, z = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        return new Vec3(x, y, z);
    }

    /// <summary>
    /// Triangulates a planar polygon with holes. Returns indices into the concatenation outer ++ holes[0] ++ …,
    /// three per triangle, wound like the outer loop. Holes are bridged into the outer loop, then the result is
    /// ear-clipped in the polygon's plane.
    /// </summary>
    public static List<int> Triangulate(IReadOnlyList<Vec3> outer, IReadOnlyList<IReadOnlyList<Vec3>>? holes = null)
    {
        var all = new List<Vec3>(outer);
        holes ??= [];
        foreach (var h in holes)
            all.AddRange(h);

        var normal = Normal(outer);
        if (normal.IsZero(1e-12) || outer.Count < 3)
            return [];
        var (u, v) = PlaneAxes(normal);
        var p2 = all.Select(p => (X: p.Dot(u), Y: p.Dot(v))).ToArray();

        // Ring of indices; outer must be counter-clockwise in (u, v) — true by construction of the axes.
        var ring = Enumerable.Range(0, outer.Count).ToList();
        var offset = outer.Count;
        var holeRings = new List<List<int>>();
        foreach (var h in holes)
        {
            var hr = Enumerable.Range(offset, h.Count).ToList();
            offset += h.Count;
            if (SignedArea(p2, hr) > 0)
                hr.Reverse(); // holes must run clockwise
            holeRings.Add(hr);
        }

        // Bridge holes from the one with the right-most vertex first (standard order, avoids crossing bridges).
        foreach (var hole in holeRings.OrderByDescending(h => h.Max(i => p2[i].X)))
            ring = Bridge(p2, ring, hole);

        return EarClip(p2, ring);
    }

    /// <summary>Two orthonormal axes spanning the plane with the given normal, so that (u, v, n) is right-handed.</summary>
    public static (Vec3 U, Vec3 V) PlaneAxes(Vec3 normal)
    {
        var n = normal.Normalized();
        var helper = Math.Abs(n.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
        var u = helper.Cross(n).Normalized();
        var v = n.Cross(u);
        return (u, v);
    }

    private static double SignedArea((double X, double Y)[] p, IReadOnlyList<int> ring)
    {
        double a = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var (x1, y1) = p[ring[i]];
            var (x2, y2) = p[ring[(i + 1) % ring.Count]];
            a += x1 * y2 - x2 * y1;
        }
        return a * 0.5;
    }

    /// <summary>Joins a hole into the ring with a zero-width bridge from its right-most vertex to a visible ring vertex.</summary>
    private static List<int> Bridge((double X, double Y)[] p, List<int> ring, List<int> hole)
    {
        var hStart = hole.IndexOf(hole.MaxBy(i => p[i].X));
        var (hx, hy) = p[hole[hStart]];

        // Cast a ray to +X; take the nearest ring edge it hits, then the visible endpoint of that edge.
        var best = -1;
        var bestX = double.PositiveInfinity;
        for (var i = 0; i < ring.Count; i++)
        {
            var (ax, ay) = p[ring[i]];
            var (bx, by) = p[ring[(i + 1) % ring.Count]];
            if ((ay > hy) == (by > hy) || ay == by)
                continue;
            var x = ax + (hy - ay) * (bx - ax) / (by - ay);
            if (x >= hx && x < bestX)
            {
                bestX = x;
                best = p[ring[i]].X > p[ring[(i + 1) % ring.Count]].X ? i : (i + 1) % ring.Count;
            }
        }

        if (best < 0)
        {
            // Degenerate input: fall back to the nearest ring vertex.
            best = Enumerable.Range(0, ring.Count).MinBy(i => Dist2(p[ring[i]], (hx, hy)));
        }
        else
        {
            // Prefer, among ring vertices inside the triangle (hole point, hit point, candidate), the one with the
            // smallest angle to the ray; it is guaranteed visible.
            var (cx, cy) = p[ring[best]];
            var tri = ((hx, hy), (bestX, hy), (cx, cy));
            var bestAngle = double.PositiveInfinity;
            for (var i = 0; i < ring.Count; i++)
            {
                var q = p[ring[i]];
                if (i == best || q.X < hx || !InTriangle(q, tri.Item1, tri.Item2, tri.Item3))
                    continue;
                var angle = Math.Abs(Math.Atan2(q.Y - hy, q.X - hx));
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = i;
                }
            }
        }

        var merged = new List<int>(ring.Count + hole.Count + 2);
        merged.AddRange(ring.Take(best + 1));
        for (var k = 0; k <= hole.Count; k++)
            merged.Add(hole[(hStart + k) % hole.Count]);
        merged.Add(ring[best]);
        merged.AddRange(ring.Skip(best + 1));
        return merged;
    }

    private static List<int> EarClip((double X, double Y)[] p, List<int> ring)
    {
        var tris = new List<int>();
        var idx = new List<int>(ring);
        var guard = 0;
        while (idx.Count > 3 && guard++ < 100_000)
        {
            var clipped = false;
            for (var i = 0; i < idx.Count; i++)
            {
                var a = idx[(i + idx.Count - 1) % idx.Count];
                var b = idx[i];
                var c = idx[(i + 1) % idx.Count];
                if (Cross(p[a], p[b], p[c]) <= 1e-12)
                    continue; // reflex or degenerate
                var ear = true;
                for (var j = 0; j < idx.Count && ear; j++)
                {
                    var q = idx[j];
                    if (q == a || q == b || q == c)
                        continue;
                    // Bridge duplicates share coordinates with a, b or c; they don't block the ear.
                    if (Same(p[q], p[a]) || Same(p[q], p[b]) || Same(p[q], p[c]))
                        continue;
                    if (InTriangle(p[q], p[a], p[b], p[c]))
                        ear = false;
                }
                if (!ear)
                    continue;
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                // Numerically stuck (collinear runs, self-touching input): drop the flattest vertex and go on.
                var flattest = Enumerable.Range(0, idx.Count).MinBy(i =>
                    Math.Abs(Cross(p[idx[(i + idx.Count - 1) % idx.Count]], p[idx[i]], p[idx[(i + 1) % idx.Count]])));
                idx.RemoveAt(flattest);
            }
        }
        if (idx.Count == 3 && Cross(p[idx[0]], p[idx[1]], p[idx[2]]) > 1e-12)
            tris.AddRange(idx);
        return tris;
    }

    private static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool InTriangle((double X, double Y) q, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        var d1 = Cross(a, b, q);
        var d2 = Cross(b, c, q);
        var d3 = Cross(c, a, q);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static bool Same((double X, double Y) a, (double X, double Y) b) => a.X == b.X && a.Y == b.Y;

    private static double Dist2((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}

/// <summary>Offset of planar polygons (SketchUp's Offset tool).</summary>
public static class PolygonOffset
{
    /// <summary>
    /// Offsets a closed planar loop inward (positive distance, towards its interior) with mitred corners. The loop
    /// winds counter-clockwise around <paramref name="normal"/>.
    /// </summary>
    public static List<Vec3> Offset(IReadOnlyList<Vec3> loop, Vec3 normal, double distance)
    {
        var n = loop.Count;
        var result = new List<Vec3>(n);
        for (var i = 0; i < n; i++)
        {
            var prev = loop[(i - 1 + n) % n];
            var cur = loop[i];
            var next = loop[(i + 1) % n];
            // Inward normals of the two edges meeting at cur (left of the direction of travel).
            var d1 = (cur - prev).Normalized();
            var d2 = (next - cur).Normalized();
            var in1 = normal.Cross(d1).Normalized();
            var in2 = normal.Cross(d2).Normalized();
            var bisector = (in1 + in2).Normalized();
            var cos = bisector.Dot(in1);
            if (bisector.IsZero(1e-12) || Math.Abs(cos) < 1e-6)
            {
                result.Add(cur + in1 * distance);
                continue;
            }
            // Mitre length grows as the corner sharpens; cap it like a miter limit of 10.
            var mitre = distance / Math.Max(cos, 0.1);
            result.Add(cur + bisector * mitre);
        }
        return result;
    }

    /// <summary>
    /// Offset as SketchUp's Offset draws it by default: the mitred outline with the parts that cross over themselves
    /// trimmed away (a notch that closes up, an arm too thin for the distance), possibly leaving several loops.
    /// </summary>
    public static List<List<Vec3>> Trimmed(IReadOnlyList<Vec3> loop, Vec3 normal, double distance)
    {
        var raw = Offset(loop, normal, distance);
        var (u, v) = Polygon.PlaneAxes(normal);
        var origin = loop[0];
        (double X, double Y) Flat(Vec3 p) => ((p - origin).Dot(u), (p - origin).Dot(v));
        Vec3 Back((double X, double Y) q) => origin + u * q.X + v * q.Y + normal * (raw[0] - origin).Dot(normal);
        var source = loop.Select(Flat).ToList();
        var sign = Math.Sign(SignedArea(source));
        var result = new List<List<Vec3>>();
        foreach (var piece in SplitAtCrossings(raw.Select(Flat).ToList()))
        {
            // A piece belongs to the offset when it keeps the outline's winding and the whole distance from it.
            if (piece.Count < 3 || Math.Sign(SignedArea(piece)) != sign || Math.Abs(SignedArea(piece)) < 1e-9)
                continue;
            var samples = piece.Select((p, i) => ((p.X + piece[(i + 1) % piece.Count].X) / 2, (p.Y + piece[(i + 1) % piece.Count].Y) / 2));
            if (samples.Any(p => DistanceToLoop(p, source) < Math.Abs(distance) * (1 - 1e-6) - 1e-9))
                continue;
            result.Add(piece.Select(Back).ToList());
        }
        return result;
    }

    /// <summary>Offsets an open chain of points sideways (left of its direction around <paramref name="normal"/>), mitred.</summary>
    public static List<Vec3> OffsetOpen(IReadOnlyList<Vec3> chain, Vec3 normal, double distance)
    {
        var result = new List<Vec3>(chain.Count);
        for (var i = 0; i < chain.Count; i++)
        {
            var d1 = i > 0 ? (chain[i] - chain[i - 1]).Normalized() : (chain[1] - chain[0]).Normalized();
            var d2 = i + 1 < chain.Count ? (chain[i + 1] - chain[i]).Normalized() : d1;
            var in1 = normal.Cross(d1).Normalized();
            var in2 = normal.Cross(d2).Normalized();
            var bisector = (in1 + in2).Normalized();
            var cos = bisector.Dot(in1);
            result.Add(bisector.IsZero(1e-12) || Math.Abs(cos) < 1e-6 ? chain[i] + in1 * distance : chain[i] + bisector * (distance / Math.Max(cos, 0.1)));
        }
        return result;
    }

    private static double SignedArea(IReadOnlyList<(double X, double Y)> p)
    {
        var a = 0.0;
        for (var i = 0; i < p.Count; i++)
        {
            var (x1, y1) = p[i];
            var (x2, y2) = p[(i + 1) % p.Count];
            a += x1 * y2 - x2 * y1;
        }
        return a / 2;
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static double DistanceToLoop((double X, double Y) p, IReadOnlyList<(double X, double Y)> loop)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / Math.Max(dx * dx + dy * dy, 1e-18), 0, 1);
            var (cx, cy) = (a.X + dx * t - p.X, a.Y + dy * t - p.Y);
            best = Math.Min(best, Math.Sqrt(cx * cx + cy * cy));
        }
        return best;
    }

    /// <summary>Splits a closed outline where two of its sides cross into simple loops (recursively).</summary>
    private static List<List<(double X, double Y)>> SplitAtCrossings(List<(double X, double Y)> points)
    {
        var p = new List<(double X, double Y)>();
        foreach (var q in points)
            if (p.Count == 0 || Dist2(p[^1], q) > 1e-18)
                p.Add(q);
        while (p.Count > 1 && Dist2(p[0], p[^1]) <= 1e-18)
            p.RemoveAt(p.Count - 1);
        var n = p.Count;
        for (var i = 0; i < n; i++)
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                    continue;
                if (Crossing(p[i], p[(i + 1) % n], p[j], p[(j + 1) % n]) is not { } x)
                    continue;
                var a = new List<(double X, double Y)> { x };
                a.AddRange(p.GetRange(i + 1, j - i));
                var b = p.GetRange(0, i + 1);
                b.Add(x);
                b.AddRange(p.GetRange(j + 1, n - j - 1));
                return [.. SplitAtCrossings(a), .. SplitAtCrossings(b)];
            }
        return [p];
    }

    private static (double X, double Y)? Crossing((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        var (rx, ry) = (b.X - a.X, b.Y - a.Y);
        var (sx, sy) = (d.X - c.X, d.Y - c.Y);
        var den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-15)
            return null;
        var t = ((c.X - a.X) * sy - (c.Y - a.Y) * sx) / den;
        var w = ((c.X - a.X) * ry - (c.Y - a.Y) * rx) / den;
        // Ends count: offsets of symmetric shapes cross exactly at corners.
        const double e = 1e-9;
        return t >= -e && t <= 1 + e && w >= -e && w <= 1 + e ? (a.X + rx * t, a.Y + ry * t) : null;
    }
}
