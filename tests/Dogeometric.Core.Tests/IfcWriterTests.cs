using System.Text.RegularExpressions;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class IfcWriterTests
{
    private static string Export(Model m, out int elements, ExportOptions? options = null)
    {
        using var w = new StringWriter();
        elements = IfcWriter.Write(m, w, "Test", options);
        return w.ToString();
    }

    private static int Count(string ifc, string entity) => Regex.Matches(ifc, $@"={entity}\(").Count;

    [Fact]
    public void Each_group_and_the_loose_geometry_become_elements_in_millimetres()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        a.Name = "Lid";
        b.Tag = model.GetOrAddTag("Hardware");
        b.Material = new Material { Name = "Steel", Color = new Rgba(128, 128, 128) };
        model.Entities.AddFace([new Vec3(0, 0, 500), new Vec3(10, 0, 500), new Vec3(10, 10, 500)]);

        var ifc = Export(model, out var elements);

        Assert.Equal(3, elements);
        Assert.Contains("FILE_SCHEMA(('IFC4'))", ifc);
        Assert.Contains("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)", ifc);
        Assert.Equal(3, Count(ifc, "IFCBUILDINGELEMENTPROXY"));
        Assert.Contains("'Lid'", ifc);
        Assert.Contains("'Ungrouped geometry'", ifc);
        Assert.Contains("IFCPRESENTATIONLAYERASSIGNMENT('Hardware'", ifc);
        // b sits 100 mm along x; its far corner is at 105.
        Assert.Contains("(105.0,5.0,5.0)", ifc);
        Assert.Contains("IFCSURFACESTYLE('Steel'", ifc);
        Assert.Matches(@"IFCRELCONTAINEDINSPATIALSTRUCTURE\('[0-9A-Za-z_$]{22}'", ifc);
    }

    [Fact]
    public void Export_selection_only_and_hidden_groups_are_respected()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        Export(model, out var only, new ExportOptions { Selection = new HashSet<object> { b } });
        Assert.Equal(1, only);

        a.Hidden = true;
        var ifc = Export(model, out var visible);
        Assert.Equal(1, visible);
        Assert.Equal(1, Count(ifc, "IFCTRIANGULATEDFACESET"));
    }
}
