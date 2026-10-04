namespace Dogeometric.Core.Geometry;

public static class Tolerance
{
    /// <summary>
    /// Two points closer than this (in mm) are the same point. SketchUp uses 0.001", about 0.0254 mm;
    /// we use 0.001 mm because enclosures need sub-0.01 mm detail.
    /// </summary>
    public const double Length = 1e-3;

    /// <summary>Angular tolerance in radians for parallel/perpendicular tests.</summary>
    public const double Angle = 1e-6;
}
