namespace Dogeometric.Core.Geometry;

/// <summary>
/// Double-precision 3D vector in model space. Model space is Z-up and measured in millimetres,
/// like SketchUp's red (X), green (Y) and blue (Z) axes.
/// </summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 UnitX = new(1, 0, 0);
    public static readonly Vec3 UnitY = new(0, 1, 0);
    public static readonly Vec3 UnitZ = new(0, 0, 1);

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

    public Vec3 Normalized()
    {
        var len = Length;
        return len > 0 ? this / len : Zero;
    }

    public bool IsZero(double tolerance = Tolerance.Length) => LengthSquared <= tolerance * tolerance;

    public double DistanceTo(Vec3 o) => (this - o).Length;

    /// <summary>Rotates this vector about a unit axis through the origin (Rodrigues' formula).</summary>
    public Vec3 RotatedAround(Vec3 axis, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return this * cos + axis.Cross(this) * sin + axis * (axis.Dot(this) * (1 - cos));
    }

    /// <summary>Rotates this point about an axis passing through <paramref name="pivot"/>.</summary>
    public Vec3 RotatedAround(Vec3 pivot, Vec3 axis, double angle) => pivot + (this - pivot).RotatedAround(axis, angle);

    /// <summary>Angle in radians between two vectors, in [0, π].</summary>
    public double AngleTo(Vec3 o)
    {
        var denom = Length * o.Length;
        return denom > 0 ? Math.Acos(Math.Clamp(Dot(o) / denom, -1, 1)) : 0;
    }

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
