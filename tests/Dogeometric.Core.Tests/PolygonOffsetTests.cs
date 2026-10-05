using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Tests;

public class PolygonOffsetTests
{
    private static List<Vec3> Loop(params (double X, double Y)[] p) => p.Select(q => new Vec3(q.X, q.Y, 0)).ToList();

    private static double Area(List<Vec3> l) => Polygon.Area(l);

    [Fact]
    public void A_square_offsets_into_one_smaller_square()
    {
        var loops = PolygonOffset.Trimmed(Loop((0, 0), (100, 0), (100, 100), (0, 100)), Vec3.UnitZ, 10);
        var loop = Assert.Single(loops);
        Assert.Equal(80 * 80, Area(loop), 6);
    }

    [Fact]
    public void A_narrow_bridge_splits_the_offset_in_two_and_a_thin_strip_leaves_nothing()
    {
        // Two 100 squares joined by a 10 wide neck: offsetting 8 inward pinches the neck off.
        var dumbbell = Loop((0, 0), (100, 0), (100, 45), (150, 45), (150, 0), (250, 0), (250, 100), (150, 100), (150, 55), (100, 55), (100, 100), (0, 100));
        var loops = PolygonOffset.Trimmed(dumbbell, Vec3.UnitZ, 8);
        Assert.Equal(2, loops.Count);
        Assert.All(loops, l => Assert.Equal(84 * 84, Area(l), 3));

        Assert.Empty(PolygonOffset.Trimmed(Loop((0, 0), (100, 0), (100, 10), (0, 10)), Vec3.UnitZ, 6));
    }

    [Fact]
    public void Offsetting_outwards_closes_a_narrow_notch()
    {
        // A U with a 10 wide slot: 8 outward fills the slot, so the outline is a plain 116 square.
        var u = Loop((0, 0), (100, 0), (100, 100), (55, 100), (55, 20), (45, 20), (45, 100), (0, 100));
        var loop = Assert.Single(PolygonOffset.Trimmed(u, Vec3.UnitZ, -8));
        Assert.Equal(116 * 116, Area(loop), 3);
        // Allowing the overlap keeps the raw outline, crossing itself in the slot.
        Assert.Equal(8, PolygonOffset.Offset(u, Vec3.UnitZ, -8).Count);
    }

    [Fact]
    public void An_open_chain_offsets_to_its_left()
    {
        var chain = PolygonOffset.OffsetOpen(Loop((0, 0), (100, 0), (100, 100)), Vec3.UnitZ, 10);
        Assert.Equal(new Vec3(0, 10, 0), chain[0]);
        Assert.Equal(new Vec3(90, 10, 0), chain[1]);
        Assert.Equal(new Vec3(90, 100, 0), chain[2]);
    }
}
