using Dogeometric.Core.Geometry;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>
/// Converts between model space (Z-up, millimetres, double) and Godot space (Y-up, metres, float).
/// Model (x, y, z) maps to Godot (x, z, -y).
/// </summary>
public static class Space
{
    public const double MetersPerUnit = 0.001;

    public static Vector3 ToGodot(Vec3 p) => new(
        (float)(p.X * MetersPerUnit),
        (float)(p.Z * MetersPerUnit),
        (float)(-p.Y * MetersPerUnit));

    public static Vector3 DirToGodot(Vec3 d) => new((float)d.X, (float)d.Z, (float)-d.Y);

    public static Vec3 FromGodot(Vector3 p) => new(p.X / MetersPerUnit, -p.Z / MetersPerUnit, p.Y / MetersPerUnit);

    public static Vec3 DirFromGodot(Vector3 d) => new(d.X, -d.Z, d.Y);
}
