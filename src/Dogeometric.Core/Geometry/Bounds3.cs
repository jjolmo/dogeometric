namespace Dogeometric.Core.Geometry;

/// <summary>Axis-aligned bounding box. <see cref="Empty"/> contains nothing.</summary>
public readonly record struct Bounds3(Vec3 Min, Vec3 Max)
{
    public static readonly Bounds3 Empty = new(
        new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vec3 Center => (Min + Max) * 0.5;
    public Vec3 Size => IsEmpty ? Vec3.Zero : Max - Min;
    public double Diagonal => Size.Length;

    public Bounds3 Include(Vec3 p) => new(
        new Vec3(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)),
        new Vec3(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));

    public Bounds3 Include(Bounds3 b) => b.IsEmpty ? this : Include(b.Min).Include(b.Max);

    /// <summary>The eight corners, numbered by bits: bit 0 picks max X, bit 1 max Y, bit 2 max Z.</summary>
    public IEnumerable<Vec3> Corners()
    {
        for (var i = 0; i < 8; i++)
            yield return new Vec3((i & 1) == 0 ? Min.X : Max.X, (i & 2) == 0 ? Min.Y : Max.Y, (i & 4) == 0 ? Min.Z : Max.Z);
    }

    public static Bounds3 FromPoints(IEnumerable<Vec3> points) => points.Aggregate(Empty, (b, p) => b.Include(p));
}
