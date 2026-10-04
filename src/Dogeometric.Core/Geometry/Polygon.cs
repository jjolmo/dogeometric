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
