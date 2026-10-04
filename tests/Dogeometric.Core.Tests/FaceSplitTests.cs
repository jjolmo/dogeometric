using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class FaceSplitTests
{
    private static List<Face> OnWall(Entities e) =>
        e.Faces.Where(f => Math.Abs(f.Normal.Normalized().Y) > 0.99 && f.OuterLoop.Points.All(p => Math.Abs(p.Y) < 1e-9)).ToList();

    [Fact]
    public void A_loop_drawn_around_a_hole_keeps_the_rest_of_the_face()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(100, 60, 40));
        StickyGeometry.DrawEdges(e, [new(30, 0, 10), new(60, 0, 10), new(60, 0, 25), new(30, 0, 25)], closed: true, -Vec3.UnitY);
        StickyGeometry.DrawEdges(e, [new(25, 0, 5), new(65, 0, 5), new(65, 0, 30), new(25, 0, 30)], closed: true, -Vec3.UnitY);
        var areas = OnWall(e).Select(f => Math.Round(f.Area)).OrderBy(a => a).ToList();
        Assert.Equal([450, 550, 3000], areas);
        Assert.Empty(SolidInspector.Find(e));
    }
}
