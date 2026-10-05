using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>
/// SketchUp's 2-Point Arc fillet: an arc tangent to the two edges meeting at a corner replaces the corner, and
/// double-clicking another corner rounds it with the same radius.
/// </summary>
public static class Fillet
{
    /// <summary>The corner's two edges and the angle between them, when <paramref name="v"/> joins exactly two that turn.</summary>
    public static (Edge A, Edge B, Vec3 DirA, Vec3 DirB)? Corner(Entities e, Vertex v)
    {
        var edges = e.Edges.Where(x => x.Start == v || x.End == v).ToList();
        if (edges.Count != 2)
            return null;
        var (a, b) = (edges[0], edges[1]);
        var da = (a.Other(v).Position - v.Position).Normalized();
        var db = (b.Other(v).Position - v.Position).Normalized();
        return Math.Abs(da.Dot(db)) > 1 - 1e-9 ? null : (a, b, da, db);
    }

    /// <summary>The radius of the fillet whose tangent points lie <paramref name="distance"/> from the corner.</summary>
    public static double RadiusFor(Vec3 dirA, Vec3 dirB, double distance) => distance * Math.Tan(Math.Acos(Math.Clamp(dirA.Dot(dirB), -1, 1)) / 2);

    /// <summary>Rounds the corner at <paramref name="v"/> with an arc of <paramref name="radius"/>; false when it doesn't fit.</summary>
    public static bool Apply(Entities e, Vertex v, double radius, int segments)
    {
        if (Corner(e, v) is not var (a, b, da, db) || radius <= Tolerance.Length)
            return false;
        var half = Math.Acos(Math.Clamp(da.Dot(db), -1, 1)) / 2;
        var reach = radius / Math.Tan(half);
        if (reach >= a.Length - Tolerance.Length || reach >= b.Length - Tolerance.Length)
            return false;
        var corner = v.Position;
        var (ta, tb) = (corner + da * reach, corner + db * reach);
        var normal = da.Cross(db).Normalized();
        var center = corner + (da + db).Normalized() * (radius / Math.Sin(half));
        // The arc turns from the first tangent point to the second the short way round, about the centre.
        var sweep = Math.PI - 2 * half;
        var from = (ta - center).Normalized();
        var turn = normal.Cross(from).Dot(tb - center) > 0 ? sweep : -sweep;
        var points = Shapes.CenterArc(center, normal, ta, turn, segments);
        points[^1] = tb;
        var drawn = StickyGeometry.DrawEdges(e, points, closed: false, normal, new Curve { Center = center, Normal = normal, Radius = radius, Segments = segments });
        if (drawn.Count == 0)
            return false;
        // What is left of the corner: the two pieces from the tangent points to it.
        var stubs = e.Edges.Where(x => (x.Start == v || x.End == v) && !drawn.Contains(x)).Cast<object>().ToList();
        Editing.Erase(e, stubs);
        return true;
    }
}
