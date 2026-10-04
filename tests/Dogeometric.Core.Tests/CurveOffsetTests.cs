using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class CurveOffsetTests
{
    private static readonly List<Vec3> Square = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)];

    [Fact]
    public void A_counter_clockwise_square_offsets_outwards_to_its_right()
    {
        var out1 = CurveOffset.Offset(Square, closed: true, Vec3.UnitZ, 1);
        Assert.Equal(144, Polygon.Area(out1), 9);
        Assert.Equal(new Vec3(-1, -1, 0), out1[0]);
        var in1 = CurveOffset.Offset(Square, closed: true, Vec3.UnitZ, -2);
        Assert.Equal(36, Polygon.Area(in1), 9);
    }

    [Fact]
    public void An_open_chain_keeps_square_ends()
    {
        List<Vec3> line = [new(0, 0, 0), new(10, 0, 0)];
        var moved = CurveOffset.Offset(line, closed: false, Vec3.UnitZ, 3);
        Assert.Equal(new Vec3(0, -3, 0), moved[0]);
        Assert.Equal(new Vec3(10, -3, 0), moved[1]);
    }

    [Fact]
    public void The_side_distance_matches_the_offset_sign()
    {
        Assert.Equal(1, CurveOffset.SideDistance(Square, true, Vec3.UnitZ, new Vec3(5, -1, 0)), 9);
        Assert.Equal(-2, CurveOffset.SideDistance(Square, true, Vec3.UnitZ, new Vec3(5, 2, 0)), 9);
    }
}
