using System.Text.RegularExpressions;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class XsiWriterTests
{
    [Fact]
    public void Groups_become_models_with_a_mesh_listing_polygons_per_material()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        a.Name = "Lid";
        b.Material = new Material { Name = "Red", Color = new Rgba(255, 0, 0), Opacity = 0.75 };
        using var w = new StringWriter();
        var meshes = XsiWriter.Write(model, w);
        var xsi = w.ToString();

        Assert.Equal(2, meshes);
        Assert.StartsWith("xsi 0300txt 0032", xsi);
        Assert.Contains("SI_Model MDL-Lid {", xsi);
        Assert.Contains("SI_Material Red {", xsi);
        Assert.Contains("SI_Material Default {", xsi);
        // A box has 8 corners and 6 face normals, and its 6 quads list 24 corners.
        Assert.Equal(2, Regex.Matches(xsi, "\t8,\n\t+\"POSITION\"").Count);
        Assert.Equal(2, Regex.Matches(xsi, "\t6,\n\t+\"NORMAL\",\n\t+\"(Red|Default)\",\n\t+24,").Count);
        Assert.Contains("105,5,5,", xsi);
    }
}
