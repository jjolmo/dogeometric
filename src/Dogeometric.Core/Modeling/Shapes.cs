using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>Points of SketchUp's circle, polygon and arc shapes.</summary>
public static class Shapes
{
    public const int DefaultCircleSegments = 24;
    public const int DefaultPolygonSides = 6;
    public const int DefaultArcSegments = 12;

    /// <summary>
    /// Regular polygon around <paramref name="center"/> in the plane of <paramref name="normal"/>; the first
    /// vertex lies along <paramref name="startDir"/>. Circumscribed polygons put the edge midpoints on the radius.
    /// </summary>
    public static List<Vec3> RegularPolygon(Vec3 center, Vec3 normal, Vec3 startDir, double radius, int sides, bool circumscribed = false)
    {
        var n = normal.Normalized();
        var u = (startDir - n * startDir.Dot(n)).Normalized();
        if (u.IsZero(1e-12))
            u = Polygon.PlaneAxes(n).U;
        var v = n.Cross(u);
        var r = circumscribed ? radius / Math.Cos(Math.PI / sides) : radius;
        var offset = circumscribed ? Math.PI / sides : 0;
        var pts = new List<Vec3>(sides);
        for (var i = 0; i < sides; i++)
        {
            var a = offset + 2 * Math.PI * i / sides;
            pts.Add(center + u * (r * Math.Cos(a)) + v * (r * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>The bulge and its side for the arc from <paramref name="start"/> to <paramref name="end"/> leaving along
    /// <paramref name="tangent"/>; null when the end lies straight ahead or behind.</summary>
    public static (double Bulge, Vec3 Side)? TangentArc(Vec3 start, Vec3 end, Vec3 tangent)
    {
        if ((end - start).Length <= Tolerance.Length)
            return null;
        var chord = (end - start).Normalized();
        var t = tangent.Normalized();
        var side = t - chord * t.Dot(chord);
        var angle = Math.Acos(Math.Clamp(t.Dot(chord), -1, 1));
        // The tangent-chord angle is half the arc's angle, so the bulge is half the chord times tan(angle / 2).
        if (side.Length < 1e-6 || angle > Math.PI * 0.99)
            return null;
        return ((end - start).Length / 2 * Math.Tan(angle / 2), side.Normalized());
    }

    /// <summary>Arc from <paramref name="start"/> to <paramref name="end"/> bulging by <paramref name="bulge"/> (2-point arc).</summary>
    public static List<Vec3> TwoPointArc(Vec3 start, Vec3 end, Vec3 bulgeDir, double bulge, int segments)
    {
        var chord = end - start;
        var half = chord.Length / 2;
        if (half <= Tolerance.Length || Math.Abs(bulge) <= Tolerance.Length)
            return [start, end];
        var mid = (start + end) * 0.5;
        var side = (bulgeDir - chord.Normalized() * bulgeDir.Dot(chord.Normalized())).Normalized() * Math.Sign(bulge);
        var h = Math.Abs(bulge);
        // Circle through start, end and the bulge apex.
        var radius = (half * half + h * h) / (2 * h);
        var center = mid + side * (h - radius);
        var a0 = (start - center).Normalized();
        var normal = a0.Cross((mid + side * h - center).Normalized()).Normalized();
        var sweep = 2 * Math.Atan2(half, radius - h);
        var pts = new List<Vec3>(segments + 1);
        for (var i = 0; i <= segments; i++)
            pts.Add(center + (start - center).RotatedAround(normal, sweep * i / segments));
        pts[^1] = end;
        return pts;
    }

    /// <summary>
    /// Arc around <paramref name="center"/> from <paramref name="start"/> turning <paramref name="sweep"/> radians about
    /// <paramref name="normal"/> (right-handed), with <paramref name="segments"/> pieces for a full turn's share.
    /// </summary>
    public static List<Vec3> CenterArc(Vec3 center, Vec3 normal, Vec3 start, double sweep, int segments)
    {
        var n = Math.Max(1, segments);
        var pts = new List<Vec3>(n + 1);
        for (var i = 0; i <= n; i++)
            pts.Add(center + (start - center).RotatedAround(normal, sweep * i / n));
        return pts;
    }

    /// <summary>
    /// The arc from <paramref name="a"/> through <paramref name="b"/> to <paramref name="c"/> (3-point arc), or null
    /// when the points are in a line.
    /// </summary>
    public static List<Vec3>? ThreePointArc(Vec3 a, Vec3 b, Vec3 c, int segments)
    {
        var ab = b - a;
        var ac = c - a;
        var normal = ab.Cross(ac);
        if (normal.Length < 1e-9)
            return null;
        // Circumcentre of the triangle.
        var n2 = normal.Dot(normal);
        var center = a + (normal.Cross(ab) * ac.Dot(ac) + ac.Cross(normal) * ab.Dot(ab)) / (2 * n2);
        var axis = normal.Normalized();
        double Angle(Vec3 p)
        {
            var u = (a - center).Normalized();
            var v = axis.Cross(u);
            var d = p - center;
            var t = Math.Atan2(d.Dot(v), d.Dot(u));
            return t < 0 ? t + 2 * Math.PI : t;
        }
        // Going from a towards b around the axis keeps b on the arc; c's angle is the sweep.
        var sweep = Angle(c);
        var pts = CenterArc(center, axis, a, sweep, segments);
        pts[^1] = c;
        return pts;
    }
}
