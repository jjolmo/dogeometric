using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class StepImportTests
{
    private static StepImport.Result Load(string name) => StepImport.Load(Path.Combine(AppContext.BaseDirectory, "Data", "step", name + ".step"));

    // FreeCAD's exact volumes; the tessellation stays within 1.5 % (a 3 mm circle as 24 sides loses 1.1 %).
    [Theory]
    [InlineData("plate", 11137.345175, 11)]
    [InlineData("sphere", 4188.790205, 1)]
    [InlineData("torus", 9869.604401, 1)]
    [InlineData("cone", 2450.442270, 3)]
    [InlineData("loft", 5948.082105, 3)]
    [InlineData("extrusion", 4574.359647, 3)]
    [InlineData("revolution", 2888.032056, 3)]
    public void Solids_come_in_closed_with_their_volume(string name, double volume, int faces)
    {
        var result = Load(name);
        Assert.Equal(faces, result.Faces);
        Assert.Equal(0, result.SkippedFaces);
        var check = MeshCheck.Analyze(MeshExtractor.Extract(result.Model));
        Assert.True(check.IsWatertight, $"{check.BoundaryEdges} open, {check.NonManifoldEdges} non-manifold, {check.MisorientedEdges} misoriented");
        Assert.InRange(check.Volume, volume * 0.985, volume * 1.005);
    }

    [Fact]
    public void Curved_faces_are_triangles_joined_by_soft_edges()
    {
        var sphere = Load("sphere").Model.Entities.Instances.Single().Definition.Entities;
        Assert.All(sphere.Edges, e => Assert.True(e.Flags.HasFlag(EdgeFlags.Soft) && e.Flags.HasFlag(EdgeFlags.Smooth)));
        var plate = Load("plate").Model.Entities.Instances.Single().Definition.Entities;
        // The flat faces come in whole: top and bottom with the hole, and the four sides.
        Assert.Equal(2, plate.Faces.Count(f => f.Loops.Count == 2));
    }

    [Fact]
    public void Assembly_parts_go_where_the_assembly_places_them()
    {
        var model = Load("assembly").Model;
        (Vec3 Min, Vec3 Max) Box(string part)
        {
            var instance = model.Entities.Instances.Single(i => i.Definition.Name == part);
            var points = MeshExtractor.Extract(model, new ExportOptions { Selection = new HashSet<object> { instance } }).SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
            return (new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)), new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
        }
        // FreeCAD's bounding boxes in the assembly's own frame: the lid tilted 10° about X and lifted 30 mm, the posts in a
        // sub-assembly turned 30° about Z at (10, 10, 5).
        var lid = Box("Lid");
        Assert.True(lid.Min.DistanceTo(new Vec3(0, -0.521, 30)) < 0.01 && lid.Max.DistanceTo(new Vec3(60, 39.392, 39.9)) < 0.01);
        var post2 = Box("Post2");
        Assert.True(post2.Min.DistanceTo(new Vec3(37.311, 24.5, 5)) < 0.01 && post2.Max.DistanceTo(new Vec3(43.311, 30.5, 17)) < 0.01);
    }

    [Fact]
    public void Our_own_step_export_reads_back()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        using var w = new StringWriter();
        StepWriter.Write(model, w);
        var back = StepImport.Read(w.ToString(), "boxes");
        Assert.Equal(12, back.Faces);
        Assert.Equal(10 * 20 * 30 + 125, MeshCheck.Analyze(MeshExtractor.Extract(back.Model)).Volume, 3);
    }

    [Fact]
    public void Colours_go_out_and_come_back()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        a.Material = new Material { Name = "Housing", Color = new Rgba(20, 40, 200) };
        var lid = b.Definition.Entities.Faces[0];
        lid.FrontMaterial = new Material { Name = "Red", Color = new Rgba(220, 0, 0) };
        using var w = new StringWriter();
        StepWriter.Write(model, w);
        var step = w.ToString();
        Assert.Equal(7, step.Split("STYLED_ITEM(").Length - 1);

        var back = StepImport.Read(step, "colours").Model;
        var faces = back.AllEntities.SelectMany(e => e.Faces).ToList();
        Assert.Equal(6, faces.Count(f => f.FrontMaterial?.Color == new Rgba(20, 40, 200)));
        Assert.Single(faces, f => f.FrontMaterial?.Color == new Rgba(220, 0, 0));
        Assert.Contains(back.Materials, m => m.Name == "Housing");
    }
}
