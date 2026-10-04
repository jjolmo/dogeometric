using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class CurviloftTests
{
    [Fact]
    public void Loft_by_spline_skins_three_rings_into_an_open_tube_facing_out()
    {
        var src = new Entities();
        foreach (var (r, z) in new[] { (20.0, 0.0), (30.0, 30.0), (10.0, 60.0) })
            StickyGeometry.DrawEdges(src, Shapes.RegularPolygon(new Vec3(0, 0, z), Vec3.UnitZ, Vec3.UnitX, r, 24, false), closed: true, Vec3.UnitZ, null);
        var chains = Curviloft.Chains(src.Edges).OrderBy(c => c[0].Z).ToList();
        Assert.Equal(3, chains.Count);
        var e = new Entities();
        Assert.Equal(24 * 12, Curviloft.LoftBySpline(e, chains, 24, 6));
        Assert.Equal(48, e.Edges.Count(x => Topology.FacesOf(e, x).Count() == 1));
        Assert.DoesNotContain(e.Edges, x => Topology.FacesOf(e, x).Count() > 2);
        // A face halfway up looks away from the tube's axis.
        var side = e.Faces.First(f => f.OuterLoop.Points.All(p => p.Z > 20 && p.Z < 40));
        var c = side.OuterLoop.Points.Aggregate(Vec3.Zero, (a, p) => a + p) / side.OuterLoop.Edges.Count;
        Assert.True(side.Normal.Dot(new Vec3(c.X, c.Y, 0)) > 0);
    }

    [Fact]
    public void Skin_contours_fills_a_loop_of_four_curves()
    {
        List<Vec3> Arc(Vec3 a, Vec3 b) => Enumerable.Range(0, 9).Select(i => { var t = i / 8.0; return a + (b - a) * t + new Vec3(0, 0, 10 * Math.Sin(Math.PI * t)); }).ToList();
        var sides = new List<List<Vec3>> { Arc(Vec3.Zero, new Vec3(40, 0, 0)), Arc(new Vec3(40, 0, 0), new Vec3(40, 40, 0)), Arc(new Vec3(40, 40, 0), new Vec3(0, 40, 0)), Arc(new Vec3(0, 40, 0), Vec3.Zero) };
        var e = new Entities();
        Assert.True(Curviloft.SkinContours(e, sides, 12) >= 144);
        Assert.Equal(48, e.Edges.Count(x => Topology.FacesOf(e, x).Count() == 1));
        Assert.Equal(20, e.Vertices.Max(v => v.Position.Z), 6);
    }

    [Fact]
    public void Curves_that_do_not_close_make_no_skin()
    {
        var e = new Entities();
        List<Vec3> L(Vec3 a, Vec3 b) => [a, b];
        Assert.Equal(0, Curviloft.SkinContours(e, [L(Vec3.Zero, new(1, 0, 0)), L(new(1, 0, 0), new(1, 1, 0)), L(new(1, 1, 0), new(0, 1, 0)), L(new(0, 2, 0), Vec3.Zero)], 4));
    }
}
