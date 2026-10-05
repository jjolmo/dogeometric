using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class PaintingTests
{
    [Fact]
    public void Deleting_a_material_unpaints_it_and_purging_keeps_those_in_use()
    {
        var model = new Model();
        var red = new Material { Name = "Red" };
        var blue = new Material { Name = "Blue" };
        var spare = new Material { Name = "Spare" };
        model.Materials.AddRange([red, blue, spare]);
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(1, 1, 1));
        model.Entities.Faces[0].FrontMaterial = red;
        model.Entities.Faces[1].BackMaterial = blue;

        Painting.DeleteMaterial(model, red);
        Assert.Null(model.Entities.Faces[0].FrontMaterial);
        Assert.DoesNotContain(red, model.Materials);

        Assert.Equal(1, Painting.PurgeMaterials(model));
        Assert.Equal([blue], model.Materials);
    }

    [Fact]
    public void Undo_brings_back_a_materials_colour()
    {
        var model = new Model();
        var m = new Material { Name = "Red", Color = new Rgba(200, 0, 0) };
        model.Materials.Add(m);
        var doc = new Document(model);
        doc.Undo.Begin("Edit Material");
        (m.Name, m.Color, m.Opacity) = ("Pink", new Rgba(255, 150, 150), 0.5);
        doc.Undo.Commit();
        doc.Undo.Undo();
        Assert.Equal(("Red", new Rgba(200, 0, 0), 1.0), (m.Name, m.Color, m.Opacity));
    }
}
