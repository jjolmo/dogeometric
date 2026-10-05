using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ThreeDsTests
{
    [Fact]
    public void Groups_come_back_as_groups_with_their_volume_and_materials()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        a.Name = "Enclosure lid";
        b.Material = new Material { Name = "Smoked", Color = new Rgba(40, 40, 40), Opacity = 0.6 };
        using var stream = new MemoryStream();
        var objects = ThreeDs.Write(model, stream);

        var back = ThreeDs.Read(stream.ToArray(), "boxes");

        Assert.Equal(2, objects);
        Assert.Equal(["Enclosure ", "Group#2"], back.Entities.Instances.Select(i => i.Name));
        Assert.All(back.Entities.Instances, i => Assert.Equal(6, i.Definition.Entities.Faces.Count));
        var check = MeshCheck.Analyze(MeshExtractor.Extract(back));
        Assert.True(check.IsWatertight);
        Assert.Equal(10 * 20 * 30 + 125, check.Volume, 2);
        var smoked = Assert.Single(back.Materials);
        Assert.Equal(new Rgba(40, 40, 40), smoked.Color);
        Assert.Equal(0.6, smoked.Opacity, 2);
    }

    [Fact]
    public void Textures_go_out_under_short_names_and_come_back_placed_as_they_were()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Box", IsGroup = true };
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(100, 50, 20));
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);
        var chip = new Material { Name = "Chip", Texture = new TextureImage { FileName = "circuit board.png", Data = [1, 2, 3], WidthMm = 40, HeightMm = 20 } };
        foreach (var f in def.Entities.Faces)
            f.FrontMaterial = chip;
        var top = def.Entities.Faces.Single(f => f.Normal.Z > 0.9);
        top.FrontMapping = TextureMapping.FromPlanePoints(Texturing.PlanePoint(top, new Vec3(10, 5, 20)),
            Texturing.PlanePoint(top, new Vec3(10, 35, 20)), Texturing.PlanePoint(top, new Vec3(-5, 5, 20)), 40, 20);
        var saved = new Dictionary<string, byte[]>();
        using var stream = new MemoryStream();
        ThreeDs.Write(model, stream, saveImage: (file, data) => saved[file] = data);

        Assert.Equal([1, 2, 3], saved["circuitb.png"]);
        var back = ThreeDs.Read(stream.ToArray(), "box", readFile: file => saved.GetValueOrDefault(file));
        var faces = back.AllEntities.SelectMany(e => e.Faces).ToList();
        Assert.Equal(6, faces.Count);
        foreach (var original in def.Entities.Faces)
        {
            var copy = faces.Single(f => f.Normal.Dot(original.Normal) > 0.99);
            Assert.NotNull(copy.FrontMaterial?.Texture);
            foreach (var p in original.OuterLoop.Points)
            {
                var (u, v) = Texturing.Uv(original, false, p, chip);
                var (u2, v2) = Texturing.Uv(copy, false, p, copy.FrontMaterial!);
                Assert.Equal(u, u2, 4);
                Assert.Equal(v, v2, 4);
            }
        }
    }

    [Fact]
    public void Meshes_over_the_vertex_limit_are_split_into_several_objects()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        using var stream = new MemoryStream();
        // Each box is 12 triangles, 36 vertices: a limit of 18 vertices makes two objects of each.
        Assert.Equal(4, ThreeDs.Write(model, stream, maxVertices: 18));
        Assert.Equal(4, ThreeDs.Read(stream.ToArray(), "split").Entities.Instances.Count);
    }
}
