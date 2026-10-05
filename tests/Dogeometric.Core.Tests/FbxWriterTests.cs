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
}
