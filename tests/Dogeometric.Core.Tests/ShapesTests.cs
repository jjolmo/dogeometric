using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ShapesTests
{
    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, -4)]
    [InlineData(-3, 8)]
    public void A_tangent_arc_leaves_along_the_tangent(double x, double y)
    {
        var start = new Vec3(0, 0, 0);
        var end = new Vec3(x, y, 0);
        var tangent = new Vec3(1, 0, 0);
        var (bulge, side) = Shapes.TangentArc(start, end, tangent)!.Value;
        var pts = Shapes.TwoPointArc(start, end, side, bulge, 96);
        Assert.Equal(end, pts[^1]);
        Assert.True((pts[1] - pts[0]).Normalized().Dot(tangent) > 0.999, $"{pts[1] - pts[0]}");
    }

    [Fact]
    public void No_tangent_arc_runs_straight_ahead()
    {
        Assert.Null(Shapes.TangentArc(Vec3.Zero, new Vec3(10, 0, 0), new Vec3(1, 0, 0)));
    }
}
