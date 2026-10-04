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
}
