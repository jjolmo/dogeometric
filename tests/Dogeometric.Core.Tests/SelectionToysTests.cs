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
}
