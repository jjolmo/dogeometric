using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>Offsets a chain of points sideways within the plane facing <c>normal</c> (Tools on Surface's Offset).</summary>
public static class CurveOffset
{
    /// <summary>
    /// The chain moved <paramref name="distance"/> to its right (seen from the normal's side, walking along it), with
    /// mitred corners; a closed chain stays closed (last point not repeated).
    /// </summary>
    public static List<Vec3> Offset(IReadOnlyList<Vec3> chain, bool closed, Vec3 normal, double distance)
    {
        var n = normal.Normalized();
        var count = chain.Count;
        var result = new List<Vec3>(count);
        Vec3 Side(Vec3 a, Vec3 b)
        {
            var t = b - a;
            t -= n * t.Dot(n);
            return t.Cross(n).Normalized();
        }
        for (var i = 0; i < count; i++)
        {
            Vec3? before = i > 0 || closed ? Side(chain[(i - 1 + count) % count], chain[i]) : null;
            Vec3? after = i < count - 1 || closed ? Side(chain[i], chain[(i + 1) % count]) : null;
            Vec3 shift;
            if (before is { } s0 && after is { } s1)
            {
                var mid = s0 + s1;
                // Nearly reversed turns would shoot far away: cap the mitre at four times the distance.
                var cos = Math.Max(mid.Dot(s0) / Math.Max(mid.Length, 1e-12), 0.25);
                shift = mid.IsZero(1e-12) ? s0 * distance : mid.Normalized() * (distance / cos);
            }
            else
                shift = (before ?? after!.Value) * distance;
            result.Add(chain[i] + shift);
        }
        return result;
    }

    /// <summary>Signed distance of <paramref name="p"/> to the chain, positive on its right (as <see cref="Offset"/>).</summary>
    public static double SideDistance(IReadOnlyList<Vec3> chain, bool closed, Vec3 normal, Vec3 p)
    {
        var n = normal.Normalized();
        var best = double.PositiveInfinity;
        var sign = 1.0;
        var segments = closed ? chain.Count : chain.Count - 1;
        for (var i = 0; i < segments; i++)
        {
            var a = chain[i];
            var b = chain[(i + 1) % chain.Count];
            var ab = b - a;
            var t = Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-12), 0, 1);
            var q = a + ab * t;
            var d = p - q;
            d -= n * d.Dot(n);
            if (d.Length < best)
            {
                best = d.Length;
                sign = d.Dot(ab.Cross(n)) >= 0 ? 1 : -1;
            }
        }
        return best * sign;
    }
}
