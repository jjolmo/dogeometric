using System.Text.RegularExpressions;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class FbxWriterTests
{
    [Fact]
    public void Each_group_is_a_null_with_a_mesh_in_y_up_millimetres()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        a.Name = "Lid";
        b.Material = new Material { Name = "Red", Color = new Rgba(255, 0, 0), Opacity = 0.75 };
        using var w = new StringWriter();
        var meshes = FbxWriter.Write(model, w);
        var fbx = w.ToString();

        Assert.Equal(2, meshes);
        Assert.StartsWith("; FBX 7.5.0 project file", fbx);
        Assert.Contains("\"Model::Lid\", \"Null\"", fbx);
        Assert.Contains("\"Model::Lid-Mesh\", \"Mesh\"", fbx);
        Assert.Contains("P: \"UnitScaleFactor\", \"double\", \"Number\", \"\",0.1", fbx);
        Assert.Contains("P: \"Opacity\", \"double\", \"Number\", \"\",0.75", fbx);
        // Box a spans y 0..20, z 0..30: Y up, its corners go out as (x, z, -y).
        Assert.Contains("10,30,-20", fbx);
        // Every object is connected: 3 nulls, 2 meshes, 2 geometries, 2 materials.
        Assert.Equal(9, Regex.Matches(fbx, "C: \"OO\"").Count);
        Assert.Equal(2, Regex.Matches(fbx, "PolygonVertexIndex: \\*36 ").Count);
    }

    [Fact]
    public void Textured_materials_get_a_texture_on_their_diffuse_colour_and_meshes_their_coordinates()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Dogeometric.Core.Geometry.Vec3.Zero, new Dogeometric.Core.Geometry.Vec3(100, 50, 20));
        var chip = new Material { Name = "Chip", Texture = new TextureImage { FileName = "chip.png", Data = [1, 2, 3], WidthMm = 40, HeightMm = 20 } };
        foreach (var f in model.Entities.Faces)
            f.FrontMaterial = chip;
        var saved = new Dictionary<string, byte[]>();
        using var w = new StringWriter();
        FbxWriter.Write(model, w, imageFolder: "box", saveImage: (path, data) => saved[path] = data);
        var fbx = w.ToString();

        Assert.Equal([1, 2, 3], saved["box/chip.png"]);
        var texture = Regex.Match(fbx, "Texture: (\\d+), \"Texture::Chip\"").Groups[1].Value;
        var material = Regex.Match(fbx, "Material: (\\d+), \"Material::Chip\"").Groups[1].Value;
        Assert.Contains($"C: \"OP\",{texture},{material}, \"DiffuseColor\"", fbx);
        Assert.Contains("RelativeFilename: \"box/chip.png\"", fbx);
        Assert.Contains("UV: *72 ", fbx);
        Assert.Contains("Type: \"LayerElementUV\"", fbx);
    }
}
