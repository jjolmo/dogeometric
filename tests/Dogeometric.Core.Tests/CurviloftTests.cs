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

    private static List<Vec3> Square(double size, double z) => [new(0, 0, z), new(size, 0, z), new(size, size, z), new(0, size, z), new(0, 0, z)];

    private static double Area(Entities e) => e.Faces.Sum(f => f.Area);

    [Fact]
    public void Loft_along_a_straight_path_makes_a_prism()
    {
        var e = new Entities();
        var made = Curviloft.LoftAlongPath(e, [Square(4, 0), Square(4, 30), [new(0, 0, 0), new(0, 0, 30)]], 16, 4);
        Assert.True(made > 0);
        Assert.Equal(4 * 4 * 30, Area(e), 6);
    }

    [Fact]
    public void Loft_along_a_path_blends_one_contour_into_the_next()
    {
        var e = new Entities();
        Curviloft.LoftAlongPath(e, [Square(4, 0), Square(8, 30), [new(0, 0, 0), new(0, 0, 30)]], 16, 6);
        var slant = Math.Sqrt(16 + 900);
        Assert.Equal(2 * 180 + 2 * (4 + 8) / 2.0 * slant, Area(e), 3);
    }

    [Fact]
    public void One_contour_sweeps_round_a_bent_path()
    {
        var e = new Entities();
        // A 2 mm square tube along an L: up 20, then along red 20.
        List<Vec3> profile = [new(0, 0, 0), new(2, 0, 0), new(2, 2, 0), new(0, 2, 0), new(0, 0, 0)];
        List<Vec3> path = [new(0, 0, 0), new(0, 0, 20), new(20, 0, 20)];
        Assert.True(Curviloft.LoftAlongPath(e, [profile, path], 8, 8) > 0);
        var b = e.Bounds();
        Assert.True(b.Size.X > 18 && b.Size.Z > 18, $"bounds {b.Size}");
        // Every point stays within the profile's size of the path.
        Assert.All(e.Vertices, v => Assert.True(Math.Min(DistanceToSegment(v.Position, path[0], path[1]), DistanceToSegment(v.Position, path[1], path[2])) < 3));
    }

    private static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
    {
        var ab = b - a;
        var t = Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    [Fact]
    public void Curves_meeting_at_a_corner_are_separate_chains()
    {
        var e = new Entities();
        StickyGeometry.DrawEdges(e, [new(0, 0, 0), new(20, 0, 0), new(20, 20, 0), new(0, 20, 0)], closed: true, Vec3.UnitZ);
        StickyGeometry.DrawEdges(e, [new(0, 0, 0), new(0, 0, 10), new(5, 0, 20)]);
        var chains = Curviloft.Chains(e.Edges);
        Assert.Equal(2, chains.Count);
        Assert.Contains(chains, c => c.Count == 5 && c[0] == c[^1]);
        Assert.Contains(chains, c => c.Count == 3);
    }
}
