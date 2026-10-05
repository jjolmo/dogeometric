using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Formats;

namespace Dogeometric.Formats.Tests;

public class CadFilesTests
{
    private static Model TwoBoxes(out ComponentInstance second)
    {
        var model = new Model();
        foreach (var (min, size) in new[] { (Vec3.Zero, new Vec3(10, 20, 30)), (new Vec3(100, 0, 0), new Vec3(5, 5, 5)) })
        {
            var def = new ComponentDefinition { Name = $"Group#{model.Definitions.Count + 1}", IsGroup = true };
            Vec3 P(double x, double y, double z) => min + new Vec3(x * size.X, y * size.Y, z * size.Z);
            var e = def.Entities;
            e.AddFace([P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)]);
            e.AddFace([P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)]);
            e.AddFace([P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]);
            e.AddFace([P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)]);
            e.AddFace([P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)]);
            e.AddFace([P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)]);
            model.Definitions.Add(def);
            model.Entities.AddInstance(def, Transform.Identity);
        }
        second = model.Entities.Instances[1];
        second.Tag = model.GetOrAddTag("Screws");
        return model;
    }

    [Theory]
    [InlineData(".dwg")]
    [InlineData(".dxf")]
    public void A_3d_export_reads_back_as_closed_faces_with_layers(string extension)
    {
        var model = TwoBoxes(out _);
        var path = Path.Combine(Path.GetTempPath(), $"dog-{Guid.NewGuid():N}{extension}");
        var (faces, lines) = CadFiles.Write3D(model, path);
        var back = extension == ".dwg" ? CadFiles.LoadDwg(path) : DxfImport.Load(path);
        File.Delete(path);

        Assert.Equal(12, faces);
        Assert.Equal(24, lines);
        var check = MeshCheck.Analyze(MeshExtractor.Extract(back));
        Assert.True(check.IsWatertight, $"faces={back.AllEntities.Sum(x => x.Faces.Count)} boundary={check.BoundaryEdges} nonmanifold={check.NonManifoldEdges} volume={check.Volume}");
        Assert.Equal(10 * 20 * 30 + 125, check.Volume, 3);
        Assert.Contains(back.Tags, t => t.Name == "Screws");
    }

    [Fact]
    public void Export_selection_only_writes_just_the_selection()
    {
        var model = TwoBoxes(out var second);
        var path = Path.Combine(Path.GetTempPath(), $"dog-{Guid.NewGuid():N}.dwg");
        var (faces, _) = CadFiles.Write3D(model, path, new ExportOptions { Selection = new HashSet<object> { second } });
        File.Delete(path);
        Assert.Equal(6, faces);
    }

    [Fact]
    public void A_face_split_by_a_circle_comes_back_as_a_face_with_a_hole_and_a_disc()
    {
        var model = new Model();
        var e = model.Entities;
        Vec3 P(double x, double y, double z) => new(x * 100, y * 50, z * 20);
        e.AddFace([P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)]);
        e.AddFace([P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)]);
        e.AddFace([P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]);
        e.AddFace([P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)]);
        e.AddFace([P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)]);
        e.AddFace([P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)]);
        StickyGeometry.DrawEdges(e, Shapes.RegularPolygon(new Vec3(50, 25, 20), Vec3.UnitZ, Vec3.UnitX, 10, 24), true, Vec3.UnitZ);
        Assert.Equal(7, e.Faces.Count);

        var path = Path.Combine(Path.GetTempPath(), $"dog-{Guid.NewGuid():N}.dwg");
        CadFiles.Write3D(model, path);
        var back = CadFiles.LoadDwg(path);
        File.Delete(path);

        var faces = back.AllEntities.SelectMany(x => x.Faces).ToList();
        Assert.Equal(7, faces.Count);
        Assert.Single(faces, f => f.Loops.Count == 2);
        var check = MeshCheck.Analyze(MeshExtractor.Extract(back));
        Assert.True(check.IsWatertight);
        Assert.Equal(100 * 50 * 20, check.Volume, 3);
    }
}
