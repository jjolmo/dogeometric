using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class IfcImportTests
{
    [Fact]
    public void Elements_come_in_classified_where_ifcopenshell_puts_them()
    {
        var result = IfcImport.Load(Path.Combine(AppContext.BaseDirectory, "Data", "ifc", "building.ifc"));
        Assert.Equal(4, result.Elements);
        Assert.Equal(["IfcWall", "IfcColumn", "IfcSlab", "IfcFurniture"], result.Model.Entities.Instances.Select(i => i.Definition.IfcType));

        // ifcopenshell's volumes and bounding boxes, in millimetres; the column's circle is 24 sides here.
        (double Volume, Vec3 Min, Vec3 Max) Measure(string type)
        {
            var instance = result.Model.Entities.Instances.Single(i => i.Definition.IfcType == type);
            var tris = MeshExtractor.Extract(result.Model, new ExportOptions { Selection = new HashSet<object> { instance } });
            var points = tris.SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
            var check = MeshCheck.Analyze(tris);
            Assert.True(check.IsWatertight);
            return (check.Volume, new Vec3(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)), new Vec3(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z)));
        }
        var wall = Measure("IfcWall");
        Assert.Equal(2e9, wall.Volume, 1);
        Assert.True(wall.Min.DistanceTo(new Vec3(900, 0, 0)) < 0.01 && wall.Max.DistanceTo(new Vec3(4464.102, 2173.205, 2500)) < 0.01);
        var box = Measure("IfcFurniture");
        Assert.Equal(6e7, box.Volume, 1);
        Assert.True(box.Min.DistanceTo(new Vec3(2717.157, 1000, 0)) < 0.01);
        Assert.InRange(Measure("IfcColumn").Volume, 211141348.4 * 0.99, 211141348.4);
        Assert.Equal(3.5e9, Measure("IfcSlab").Volume, 1);
    }

    [Fact]
    public void Our_ifc_export_reads_back_with_types_and_colours()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();
        Classification.Apply(model, a, "IfcDoor");
        b.Material = new Material { Name = "Steel", Color = new Rgba(128, 128, 128) };
        using var w = new StringWriter();
        IfcWriter.Write(model, w);

        var back = IfcImport.Read(w.ToString(), "boxes").Model;
        Assert.Equal(["IfcDoor", "IfcBuildingElementProxy"], back.Entities.Instances.Select(i => i.Definition.IfcType));
        Assert.Equal(10 * 20 * 30 + 125, MeshCheck.Analyze(MeshExtractor.Extract(back)).Volume, 3);
        Assert.Contains(back.Materials, m => m.Color == new Rgba(128, 128, 128));
    }
}
