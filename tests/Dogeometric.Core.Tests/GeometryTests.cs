using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class GeometryTests
{
    private static double TriangulatedArea(IReadOnlyList<Vec3> outer, IReadOnlyList<IReadOnlyList<Vec3>>? holes = null)
    {
        var all = outer.Concat(holes?.SelectMany(h => h) ?? []).ToList();
        var idx = Polygon.Triangulate(outer, holes);
        double area = 0;
        for (var i = 0; i < idx.Count; i += 3)
            area += (all[idx[i + 1]] - all[idx[i]]).Cross(all[idx[i + 2]] - all[idx[i]]).Length / 2;
        return area;
    }

    [Fact]
    public void Square_triangulates_into_two_triangles()
    {
        Vec3[] square = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)];
        Assert.Equal(6, Polygon.Triangulate(square).Count);
        Assert.Equal(100, TriangulatedArea(square), 9);
    }

    [Fact]
    public void Concave_polygon_area_is_preserved()
    {
        // L shape, 3 squares of 10×10.
        Vec3[] l = [new(0, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0), new(10, 20, 0), new(0, 20, 0)];
        Assert.Equal(300, TriangulatedArea(l), 9);
        Assert.Equal(300, Polygon.Area(l), 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Holes_whose_bridges_meet_at_one_corner_or_run_along_a_side_are_all_cut(bool squares)
    {
        // From a STEP part: the second hole's bridge reaches the corner the first one already uses, or runs along its side.
        Vec3[] outer = [new(-10, 145, 0), new(5, 145, 0), new(10, 140, 0), new(20, 140, 0), new(20, 100, 0), new(-10, 100, 0)];
        List<Vec3> Hole(double cx, double cy, double r) => squares
            ? [new(cx - r, cy - r, 0), new(cx - r, cy + r, 0), new(cx + r, cy + r, 0), new(cx + r, cy - r, 0)]
            : Enumerable.Range(0, 24).Select(i => new Vec3(cx + r * Math.Cos(i * Math.PI / 12), cy + r * Math.Sin(i * Math.PI / 12), 0)).ToList();
        IReadOnlyList<Vec3>[] holes = [Hole(5, 107, 1.5), Hole(6, 114, 0.5)];
        Assert.Equal(Polygon.Area(outer) - holes.Sum(Polygon.Area), TriangulatedArea(outer, holes), 6);
    }

    [Fact]
    public void A_side_sampled_into_collinear_points_keeps_every_point()
    {
        // A face of a STEP part whose straight edge comes sampled as a curve: dropping one of its points left a gap.
        Vec3[] outer =
        [
            new(31.3720075856, 104.641463353, 1.3036405863),
            new(32.354864476, 105.624320244, 1.3036405863),
            new(32.8317054694, 105.14747925, 1.3468685863),
            new(31.8493821247, 104.165155906, 1.3468685863),
            new(31.789710307308596, 104.22469433687888, 1.3414650862996467),
            new(31.730038489922396, 104.2842327677526, 1.3360615862997645),
            new(31.670366672536204, 104.34377119862631, 1.3306580862998822),
            new(31.61069485515, 104.40330962950001, 1.3252545863),
            new(31.551023037763805, 104.46284806037372, 1.3198510863001178),
            new(31.491351220377602, 104.5223864912474, 1.3144475863002356),
            new(31.431679402991403, 104.58192492212112, 1.3090440863003534),
        ];
        var idx = Polygon.Triangulate(outer);
        Assert.Equal(Enumerable.Range(0, outer.Length), idx.Distinct().Order());
        Assert.Equal(Polygon.Area(outer), TriangulatedArea(outer), 9);
    }

    [Fact]
    public void Holes_are_subtracted()
    {
        Vec3[] outer = [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)];
        Vec3[] hole1 = [new(10, 10, 0), new(30, 10, 0), new(30, 30, 0), new(10, 30, 0)];
        Vec3[] hole2 = [new(60, 60, 0), new(60, 80, 0), new(80, 80, 0), new(80, 60, 0)]; // opposite winding on purpose
        Assert.Equal(10000 - 400 - 400, TriangulatedArea(outer, [hole1, hole2]), 6);
    }

    [Fact]
    public void Triangulation_works_on_tilted_planes()
    {
        var rot = Transform.Rotation(new Vec3(1, 2, 3), 0.7);
        var pts = new Vec3[] { new(0, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0), new(10, 20, 0), new(0, 20, 0) }
            .Select(rot.ApplyPoint).ToList();
        Assert.Equal(300, TriangulatedArea(pts), 6);
    }

    [Fact]
    public void Newell_normal_follows_winding()
    {
        Vec3[] ccw = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)];
        Assert.Equal(1, Polygon.Normal(ccw).Z, 12);
        Assert.Equal(-1, Polygon.Normal(ccw.Reverse().ToList()).Z, 12);
    }

    [Fact]
    public void Transform_inverse_round_trips()
    {
        var t = Transform.Rotation(new Vec3(0, 0, 1), 0.3, new Vec3(5, 5, 0)).Then(Transform.Translation(new Vec3(1, 2, 3)));
        var p = new Vec3(7, -3, 11);
        Assert.True(t.Inverse().ApplyPoint(t.ApplyPoint(p)).DistanceTo(p) < 1e-9);
    }

    [Fact]
    public void Rotation_keeps_its_pivot_fixed()
    {
        var pivot = new Vec3(10, 20, 30);
        var t = Transform.Rotation(new Vec3(1, 1, 0), 1.1, pivot);
        Assert.True(t.ApplyPoint(pivot).DistanceTo(pivot) < 1e-9);
    }

    [Fact]
    public void Then_applies_inner_transform_first()
    {
        var scale = Transform.Scaling(2, 2, 2);
        var move = Transform.Translation(new Vec3(10, 0, 0));
        Assert.Equal(new Vec3(12, 0, 0), scale.Then(move).ApplyPoint(Vec3.UnitX));
        Assert.Equal(new Vec3(22, 0, 0), move.Then(scale).ApplyPoint(Vec3.UnitX));
    }

    [Fact]
    public void Negative_scale_is_mirroring()
    {
        Assert.True(Transform.Scaling(-1, 1, 1).IsMirroring);
        Assert.False(Transform.Rotation(Vec3.UnitZ, 2).IsMirroring);
    }

    [Fact]
    public void Column_major_round_trip()
    {
        var t = Transform.Rotation(new Vec3(1, 0, 1), 0.5).Then(Transform.Translation(new Vec3(4, 5, 6)));
        var back = Transform.FromColumnMajor(t.ToColumnMajor());
        Assert.True(back.ApplyPoint(new Vec3(1, 2, 3)).DistanceTo(t.ApplyPoint(new Vec3(1, 2, 3))) < 1e-12);
    }

    [Fact]
    public void Faces_share_vertices_and_edges()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        Assert.Equal(8, e.Vertices.Count);
        Assert.Equal(12, e.Edges.Count);
        Assert.Equal(6, e.Faces.Count);
        // Every edge borders exactly two faces of a closed box.
        Assert.All(e.Edges, edge => Assert.Equal(2, e.Faces.Count(f => f.Loops.Any(l => l.Edges.Any(x => x.Edge == edge)))));
    }

    [Fact]
    public void Box_faces_point_outwards()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        var center = new Vec3(5, 5, 5);
        Assert.All(e.Faces, f => Assert.True(f.Normal.Dot(f.OuterLoop.Points.First() - center) > 0));
    }

    [Fact]
    public void Bounds_include_transformed_instances()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        var b = model.Entities.Bounds();
        Assert.Equal(new Vec3(0, 0, 0), b.Min);
        Assert.Equal(new Vec3(105, 20, 30), b.Max);
    }

    [Fact]
    public void Three_point_arc_runs_through_its_points_on_one_circle()
    {
        Vec3 a = new(10, 0, 0), b = new(0, 10, 0), c = new(-10, 0, 0);
        var arc = Shapes.ThreePointArc(a, b, c, 12)!;
        Assert.Equal(a, arc[0]);
        Assert.Equal(c, arc[^1]);
        Assert.All(arc, p => Assert.Equal(10, p.Length, 6));
        Assert.Contains(arc, p => p.DistanceTo(b) < 1e-6);
        Assert.Null(Shapes.ThreePointArc(a, new Vec3(5, 0, 0), c, 12));
    }
}
