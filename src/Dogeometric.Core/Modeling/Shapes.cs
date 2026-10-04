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
}
