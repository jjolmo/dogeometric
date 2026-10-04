namespace Dogeometric.Core.Geometry;

/// <summary>
/// Affine 3D transform (rotation/scale/shear + translation), double precision. Stored as the upper 3×4 of a
/// 4×4 matrix: columns X, Y, Z are the transformed axes, <see cref="Origin"/> the translation.
/// </summary>
public readonly record struct Transform(Vec3 X, Vec3 Y, Vec3 Z, Vec3 Origin)
{
    public static readonly Transform Identity = new(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, Vec3.Zero);

    public static Transform Translation(Vec3 offset) => Identity with { Origin = offset };

    public static Transform Scaling(double sx, double sy, double sz) =>
        new(Vec3.UnitX * sx, Vec3.UnitY * sy, Vec3.UnitZ * sz, Vec3.Zero);

    public static Transform Rotation(Vec3 axis, double angle, Vec3 pivot = default)
    {
        var a = axis.Normalized();
        var x = Vec3.UnitX.RotatedAround(a, angle);
        var y = Vec3.UnitY.RotatedAround(a, angle);
        var z = Vec3.UnitZ.RotatedAround(a, angle);
        var origin = pivot - (x * pivot.X + y * pivot.Y + z * pivot.Z);
        return new Transform(x, y, z, origin);
    }

    /// <summary>Builds from a 16-element column-major 4×4 matrix (SketchUp/OpenSKP layout).</summary>
    public static Transform FromColumnMajor(IReadOnlyList<double> m)
    {
        if (m.Count < 16)
            return Identity;
        // SketchUp stores a homogeneous w in m[15]; non-1 w scales the whole transform uniformly.
        var w = m[15] == 0 ? 1 : m[15];
        return new Transform(
            new Vec3(m[0], m[1], m[2]) / w,
            new Vec3(m[4], m[5], m[6]) / w,
            new Vec3(m[8], m[9], m[10]) / w,
            new Vec3(m[12], m[13], m[14]) / w);
    }

    /// <summary>
    /// Builds from SketchUp's 13-element layout (as OpenSKP reads it): row-major 3×3 rotation/scale, translation,
    /// then a homogeneous scale w that divides the whole transform.
    /// </summary>
    public static Transform FromSketchUp13(IReadOnlyList<double> m)
    {
        if (m.Count < 12)
            return Identity;
        var w = m.Count > 12 && m[12] != 0 ? m[12] : 1;
        return new Transform(
            new Vec3(m[0], m[3], m[6]) / w,
            new Vec3(m[1], m[4], m[7]) / w,
            new Vec3(m[2], m[5], m[8]) / w,
            new Vec3(m[9], m[10], m[11]) / w);
    }

    public double[] ToColumnMajor() =>
    [
        X.X, X.Y, X.Z, 0,
        Y.X, Y.Y, Y.Z, 0,
        Z.X, Z.Y, Z.Z, 0,
        Origin.X, Origin.Y, Origin.Z, 1,
    ];

    public Vec3 ApplyPoint(Vec3 p) => X * p.X + Y * p.Y + Z * p.Z + Origin;

    public Vec3 ApplyVector(Vec3 v) => X * v.X + Y * v.Y + Z * v.Z;

    /// <summary>Normals transform by the inverse transpose, which keeps them perpendicular under non-uniform scale.</summary>
    public Vec3 ApplyNormal(Vec3 n)
    {
        var inv = Inverse();
        return new Vec3(inv.X.Dot(n), inv.Y.Dot(n), inv.Z.Dot(n)).Normalized();
    }

    public double Determinant => X.Dot(Y.Cross(Z));

    /// <summary>True when the transform mirrors geometry (faces must flip their winding).</summary>
    public bool IsMirroring => Determinant < 0;

    /// <summary>Composition: applies this transform first, then <paramref name="outer"/>.</summary>
    public Transform Then(Transform outer) => new(
        outer.ApplyVector(X), outer.ApplyVector(Y), outer.ApplyVector(Z), outer.ApplyPoint(Origin));

    public Transform Inverse()
    {
        var det = Determinant;
        if (Math.Abs(det) < 1e-18)
            throw new InvalidOperationException("Transform is not invertible");
        // Rows of the inverse 3×3 are the cross products of the columns divided by the determinant.
        var r0 = Y.Cross(Z) / det;
        var r1 = Z.Cross(X) / det;
        var r2 = X.Cross(Y) / det;
        var cx = new Vec3(r0.X, r1.X, r2.X);
        var cy = new Vec3(r0.Y, r1.Y, r2.Y);
        var cz = new Vec3(r0.Z, r1.Z, r2.Z);
        var origin = -(cx * Origin.X + cy * Origin.Y + cz * Origin.Z);
        return new Transform(cx, cy, cz, origin);
    }
}
