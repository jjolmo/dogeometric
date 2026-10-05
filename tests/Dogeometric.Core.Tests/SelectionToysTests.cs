using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SelectionToysTests
{
    private static Entities Box()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 20, 30));
        return e;
    }

    private static Face Facing(Entities e, Vec3 n) => e.Faces.Single(f => f.Normal.Normalized().Dot(n) > 0.99);

    [Fact]
    public void Select_only_and_deselect_filter_by_kind()
    {
        var e = Box();
        var all = e.Faces.Cast<object>().Concat(e.Edges).ToList();
        Assert.Equal(6, SelectionToys.Only(SelectionKind.Faces, e, all).Count);
        Assert.Equal(12, SelectionToys.Without(SelectionKind.Faces, e, all).Count);
        Assert.Empty(SelectionToys.Only(SelectionKind.BorderEdges, e, all));
    }

    [Fact]
    public void Selection_border_is_the_outline_of_the_selected_faces()
    {
        var e = Box();
        var top = Facing(e, Vec3.UnitZ);
        var sel = Topology.EdgesOf(top).Cast<object>().Append(top).ToList();
        Assert.Equal(4, SelectionToys.Only(SelectionKind.SelectionBorder, e, sel).Count);
    }

    [Fact]
    public void Related_faces()
    {
        var e = Box();
        var top = Facing(e, Vec3.UnitZ);
        Assert.Equal(2, SelectionToys.Faces(e, [top], SelectionToys.FaceRelation.Parallel).Count);
        Assert.Single(SelectionToys.Faces(e, [top], SelectionToys.FaceRelation.SameDirection));
        Assert.Equal(5, SelectionToys.Faces(e, [top], SelectionToys.FaceRelation.Perpendicular).Count);
        Assert.Equal(5, SelectionToys.ConnectedFaces(e, [top], SelectionToys.FaceRelation.Perpendicular).Count);
        Assert.Same(Facing(e, -Vec3.UnitZ), Assert.Single(SelectionToys.OppositeFaces(e, [top])));
    }

    [Fact]
    public void Group_copies_and_conversion_to_components()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        var copy = model.Entities.AddInstance(a.Definition, Transform.Translation(new Vec3(0, 0, 500)));
        var copies = SelectionToys.Copies(model.Entities, [a], groups: true, sameTag: false);
        Assert.Equal(2, copies.Count);
        Assert.Contains(copy, copies);
        Assert.Equal(1, SelectionToys.GroupsToComponents(model, [a]));
        Assert.False(a.Definition.IsGroup);
        Assert.False(copy.IsGroup);
        Assert.True(b.IsGroup);
    }

    /// <summary>A 12-sided prism: 12 quads around, 12-gon caps.</summary>
    private static Entities Prism()
    {
        var e = new Entities();
        var w = new Welder(e);
        var bottom = Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 10, 12);
        var top = bottom.Select(p => p + new Vec3(0, 0, 20)).ToList();
        for (var i = 0; i < 12; i++)
        {
            var j = (i + 1) % 12;
            w.Face([bottom[i], bottom[j], top[j], top[i]], []);
        }
        w.Face(bottom, []);
        w.Face(top, []);
        return e;
    }

    private static Edge Vertical(Entities e) => e.Edges.First(x => Math.Abs(x.Start.Position.Z - x.End.Position.Z) > 1);

    [Fact]
    public void A_quad_face_loop_goes_round_the_prism_and_stops_at_its_caps()
    {
        var e = Prism();
        Assert.Equal(12, SelectionToys.QuadFaceLoops(e, [Vertical(e)]).Count);
        var rim = e.Edges.First(x => x.Start.Position.Z == 0 && x.End.Position.Z == 0);
        Assert.Single(SelectionToys.QuadFaceLoops(e, [rim]));
    }

    [Fact]
    public void Connected_faces_by_angle_take_the_curved_side_but_not_the_caps()
    {
        var e = Prism();
        var side = e.Faces.First(f => f.OuterLoop.Edges.Count == 4);
        Assert.Equal(12, SelectionToys.ConnectedFacesByAngle(e, [side], 31).Count);
        Assert.Single(SelectionToys.ConnectedFacesByAngle(e, [side], 29));
        Assert.Equal(14, SelectionToys.ConnectedFacesByAngle(e, [side], 91).Count);
    }

    [Fact]
    public void Edge_loops_take_the_loops_through_the_selection()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        Assert.Equal(4, SelectionToys.EdgeLoops(e, [e.Faces[0]]).Count);
        Assert.Equal(7, SelectionToys.EdgeLoops(e, [e.Edges[0]]).Count);
    }
}
