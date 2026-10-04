using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SolidInspectorTests
{
    private static Entities Box()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 20, 30));
        return e;
    }

    private static List<SolidErrorKind> Kinds(Entities e) => SolidInspector.Find(e).Select(x => x.Kind).ToList();

    [Fact]
    public void A_closed_box_has_no_errors() => Assert.Empty(SolidInspector.Find(Box()));

    [Fact]
    public void A_reversed_face_is_found_and_turned_back_out()
    {
        var e = Box();
        var top = e.Faces.Single(f => f.Normal.Z > 0.5);
        FaceFinder.Reverse(top);
        var errors = SolidInspector.Find(e);
        Assert.Equal([SolidErrorKind.ReversedFace], errors.Select(x => x.Kind));
        Assert.Same(top, errors[0].Entities[0]);
        Assert.True(SolidInspector.Fix(e, errors));
        Assert.True(top.Normal.Z > 0.5);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Every_face_reversed_is_reported_too()
    {
        var e = Box();
        foreach (var f in e.Faces)
            FaceFinder.Reverse(f);
        Assert.Equal(6, Kinds(e).Count(k => k == SolidErrorKind.ReversedFace));
    }

    [Fact]
    public void A_missing_face_leaves_a_surface_border()
    {
        var e = Box();
        e.Faces.Remove(e.Faces.Single(f => f.Normal.Z > 0.5));
        var errors = SolidInspector.Find(e);
        var border = Assert.Single(errors);
        Assert.Equal(SolidErrorKind.SurfaceBorder, border.Kind);
        Assert.Equal(4, border.Entities.Count);
        Assert.False(SolidInspector.Fix(e, errors));
    }

    [Fact]
    public void Stray_edges_and_hidden_faces_are_fixed()
    {
        var e = Box();
        e.AddEdge(e.AddVertex(new Vec3(50, 0, 0)), e.AddVertex(new Vec3(60, 0, 0)));
        e.Faces[0].Hidden = true;
        var errors = SolidInspector.Find(e);
        Assert.Equal([SolidErrorKind.HiddenFace, SolidErrorKind.StrayEdge], errors.Select(x => x.Kind).Order());
        Assert.True(SolidInspector.Fix(e, errors));
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(12, e.Edges.Count);
    }

    [Fact]
    public void A_wall_inside_two_boxes_is_internal_and_erased()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        TestModels.Box(e, new Vec3(10, 0, 0), new Vec3(10, 10, 10));
        // The shared wall is there twice (one per box); keep a single one so its edges are shared by three faces.
        var walls = e.Faces.Where(f => f.Loops[0].Points.All(p => Math.Abs(p.X - 10) < 1e-9)).ToList();
        foreach (var extra in walls.Skip(1))
            e.Faces.Remove(extra);
        var wall = walls[0];

        var errors = SolidInspector.Find(e);
        var inner = Assert.Single(errors);
        Assert.Equal(SolidErrorKind.InternalFace, inner.Kind);
        Assert.Same(wall, inner.Entities[0]);
        Assert.True(SolidInspector.Fix(e, errors));
        Assert.DoesNotContain(wall, e.Faces);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void A_fin_sticking_out_is_an_external_face()
    {
        var e = Box();
        // A flap hinged on the top front edge, standing out in front of the box.
        var fin = e.AddFace([new Vec3(0, 0, 30), new Vec3(10, 0, 30), new Vec3(10, -15, 30), new Vec3(0, -15, 30)]);
        var errors = SolidInspector.Find(e);
        Assert.Contains(errors, x => x.Kind == SolidErrorKind.ExternalFace && x.Entities[0] == fin);
        Assert.True(SolidInspector.Fix(e, errors.Where(x => x.Kind == SolidErrorKind.ExternalFace)));
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(12, e.Edges.Count);
    }

    [Fact]
    public void A_hole_in_a_face_is_healed()
    {
        var e = Box();
        var top = e.Faces.Single(f => f.Normal.Z > 0.5);
        var hole = new FaceLoop();
        Vec3[] pts = [new(2, 2, 30), new(2, 4, 30), new(4, 4, 30), new(4, 2, 30)];
        for (var i = 0; i < 4; i++)
            hole.Edges.Add((e.AddEdge(e.VertexAt(pts[i]), e.VertexAt(pts[(i + 1) % 4])), false));
        top.Loops.Add(hole);

        var errors = SolidInspector.Find(e);
        var found = Assert.Single(errors);
        Assert.Equal(SolidErrorKind.FaceHole, found.Kind);
        Assert.True(SolidInspector.Fix(e, errors));
        Assert.Single(top.Loops);
        Assert.Contains(top, e.Faces);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Nested_groups_are_reported_but_not_fixed()
    {
        var e = Box();
        e.AddInstance(new ComponentDefinition { IsGroup = true }, Transform.Identity);
        var errors = SolidInspector.Find(e);
        Assert.Equal([SolidErrorKind.NestedInstance], errors.Select(x => x.Kind));
        Assert.False(SolidInspector.Fix(e, errors));
    }

    [Fact]
    public void Short_edges_only_when_asked()
    {
        var e = new Entities();
        TestModels.Box(e, Vec3.Zero, new Vec3(1, 20, 30));
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(4, SolidInspector.Find(e, SolidInspector.DefaultShortEdge).Count(x => x.Kind == SolidErrorKind.ShortEdge));
    }

    [Fact]
    public void Reversing_a_face_keeps_its_texture_where_it_was()
    {
        var e = Box();
        var side = e.Faces.Single(f => f.Normal.Y < -0.5);
        var material = new Material { Texture = new TextureImage { WidthMm = 7, HeightMm = 5 } };
        side.FrontMaterial = material;
        side.FrontMapping = TextureMapping.FromPlanePoints((1, 2), (8, 3), (0, 7), 7, 5);
        var p = new Vec3(3, 0, 11);
        var before = Texturing.Uv(side, back: false, p, material);
        FaceFinder.Reverse(side);
        var after = Texturing.Uv(side, back: true, p, material);
        Assert.Equal(before.U, after.U, 9);
        Assert.Equal(before.V, after.V, 9);

        var top = e.Faces.Single(f => f.Normal.Z > 0.5);
        top.FrontMaterial = material;
        top.FrontMapping = TextureMapping.FromPlanePoints((1, 2), (8, 3), (0, 7), 7, 5);
        var q = new Vec3(3, 4, 30);
        before = Texturing.Uv(top, back: false, q, material);
        FaceFinder.Reverse(top);
        after = Texturing.Uv(top, back: true, q, material);
        Assert.Equal(before.U, after.U, 9);
        Assert.Equal(before.V, after.V, 9);
    }
}
