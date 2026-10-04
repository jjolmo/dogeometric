using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class CleanUpTests
{
    /// <summary>Two boxes side by side without the wall between them, and a stray edge.</summary>
    private static Model Glued()
    {
        var model = new Model();
        var e = model.Entities;
        TestModels.Box(e, Vec3.Zero, new Vec3(10, 10, 10));
        TestModels.Box(e, new Vec3(10, 0, 0), new Vec3(10, 10, 10));
        e.Faces.RemoveAll(f => f.OuterLoop.Points.All(p => Math.Abs(p.X - 10) < 1e-9));
        e.AddEdge(e.VertexAt(new Vec3(50, 0, 0)), e.VertexAt(new Vec3(60, 0, 0)));
        return model;
    }

    [Fact]
    public void Defaults_leave_a_clean_box()
    {
        var model = Glued();
        var e = model.Entities;
        var stats = CleanUp.Run(model, e, [], new CleanUpOptions());
        Assert.Equal(6, e.Faces.Count);
        Assert.Equal(12, e.Edges.Count);
        Assert.Equal(8, e.Vertices.Count);
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(9, stats["Edges Reduced"]);
    }

    [Fact]
    public void Coplanar_faces_with_different_materials_stay_apart()
    {
        var model = Glued();
        var e = model.Entities;
        var red = new Material { Name = "Red" };
        model.Materials.Add(red);
        e.Faces.First(f => f.Normal.Z > 0.5).FrontMaterial = red;
        CleanUp.Run(model, e, [], new CleanUpOptions());
        Assert.Equal(7, e.Faces.Count);
        Assert.Contains(red, model.Materials);
    }

    [Fact]
    public void Merging_two_coplanar_faces_keeps_the_merged_face()
    {
        var e = new Entities();
        e.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        e.AddFace([new(10, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0)]);
        Assert.Equal(1, CleanUp.MergeCoplanarFaces(e, null, new CleanUpOptions()));
        Assert.Equal(200, Assert.Single(e.Faces).Area, 6);
    }

    [Fact]
    public void Identical_materials_merge_and_unused_ones_are_purged()
    {
        var model = new Model();
        var a = new Material { Name = "A", Color = new Rgba(10, 20, 30) };
        var b = new Material { Name = "B", Color = new Rgba(10, 20, 30) };
        var unused = new Material { Name = "C", Color = new Rgba(1, 2, 3) };
        model.Materials.AddRange([a, b, unused]);
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(1, 1, 1));
        model.Entities.Faces[0].FrontMaterial = b;
        CleanUp.Run(model, model.Entities, [], new CleanUpOptions { MergeMaterials = true });
        Assert.Equal([a], model.Materials);
        Assert.Same(a, model.Entities.Faces[0].FrontMaterial);
    }
}
