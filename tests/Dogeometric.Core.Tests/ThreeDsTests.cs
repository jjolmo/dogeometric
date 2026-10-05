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
    public void Meshes_over_the_vertex_limit_are_split_into_several_objects()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        using var stream = new MemoryStream();
        // Each box is 12 triangles, 36 vertices: a limit of 18 vertices makes two objects of each.
        Assert.Equal(4, ThreeDs.Write(model, stream, maxVertices: 18));
        Assert.Equal(4, ThreeDs.Read(stream.ToArray(), "split").Entities.Instances.Count);
    }
}
