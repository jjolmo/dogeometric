using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ReportsTests
{
    [Fact]
    public void A_report_lists_components_with_their_sizes_and_solid_volumes()
    {
        var model = new Model();
        var part = new ComponentDefinition { Name = "Lid" };
        TestModels.Box(part.Entities, Vec3.Zero, new Vec3(40, 30, 2));
        model.Definitions.Add(part);
        var housing = new ComponentDefinition { Name = "Housing", IsGroup = true };
        housing.Entities.AddInstance(part, Transform.Translation(new Vec3(0, 0, 20)));
        model.Definitions.Add(housing);
        model.Entities.AddInstance(housing, Transform.Identity).Name = "Enclosure";
        model.Entities.AddInstance(part, Transform.Translation(new Vec3(100, 0, 0)));

        var rows = Reports.Rows(model);
        Assert.Equal(3, rows.Count);
        var nested = rows.Single(r => r.Path == "Enclosure > Lid");
        Assert.Equal(("Component", 40.0, 30.0, 2.0), (nested.Kind, nested.LenX, nested.LenY, nested.LenZ));
        Assert.Equal(2400, nested.Volume!.Value, 6);
        Assert.Equal(2400, rows.Single(r => r.Path == "Enclosure").Volume!.Value, 6);

        var csv = Reports.ToCsv(rows).Split('\n');
        Assert.StartsWith("Path,Entity Description", csv[0]);
        Assert.Contains("Enclosure > Lid,Component,Lid,,Untagged,,2400,40,30,2", csv);
        Assert.Contains("<td>Enclosure &gt; Lid</td>", Reports.ToHtml(rows, "Report"));
    }
}
