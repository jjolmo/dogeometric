using System.IO.Compression;
using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ClassificationTests
{
    /// <summary>A .skc laid out as SketchUp's: document.xml naming the XSD, its title, the XSD and maybe its filter.</summary>
    private static MemoryStream Skc(string title, string xsd, string? filter)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string name, string text)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                w.Write(text);
            }
            Entry("document.xml", """
                <classificationDocument xmlns="http://www.sketchup.com/schemas/sketchup/1.0/classification" xmlns:cls="http://www.sketchup.com/schemas/sketchup/1.0/classification">
                  <cls:Classification xsdFile="Schemas/Test.xsd"></cls:Classification>
                </classificationDocument>
                """);
            Entry("documentProperties.xml", $"""
                <documentProperties xmlns="http://www.sketchup.com/schemas/1.0/documentproperties" xmlns:dp="http://www.sketchup.com/schemas/1.0/documentproperties">
                  <dp:title>{title}</dp:title><dp:description>Test schema</dp:description>
                </documentProperties>
                """);
            Entry("Schemas/Test.xsd", xsd);
            if (filter != null)
                Entry("Schemas/Test.xsd.filter", filter);
        }
        stream.Position = 0;
        return stream;
    }

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
          <xs:element name="Ss_25_10" type="xs:string"/>
          <xs:element name="Pr_20_93" type="xs:string"/>
          <xs:complexType name="NotAType"/>
        </xs:schema>
        """;

    [Fact]
    public void A_filter_lists_the_types_and_their_attributes_and_skips_the_blacklist()
    {
        using var skc = Skc("Uniclass", Xsd, "Ss_25_10\n{\n  Name\n  Tag\n}\n\n// Blacklist Attributes\n{\n  href\n}\n");
        var schema = SkcFile.Read(skc, "fallback");
        Assert.Equal("Uniclass", schema.Name);
        Assert.Equal("Test schema", schema.Description);
        var (type, attributes) = Assert.Single(schema.Types);
        Assert.Equal("Ss_25_10", type);
        Assert.Equal(["Name", "Tag"], attributes);
    }

    [Fact]
    public void Without_a_filter_every_top_level_element_is_a_type()
    {
        using var skc = Skc("Uniclass", Xsd, null);
        Assert.Equal(["Ss_25_10", "Pr_20_93"], SkcFile.Read(skc, "fallback").Types.Select(t => t.Type));
    }

    [Fact]
    public void Types_go_by_schema_IFC_ones_to_the_IFC_type_and_Ctrl_erases_them_all()
    {
        var model = new Model();
        var def = new ComponentDefinition { Name = "Wall", IsGroup = true };
        model.Definitions.Add(def);
        var wall = model.Entities.AddInstance(def, Transform.Identity);

        Classification.Apply(model, wall, "Ss_25_10", schema: "Uniclass");
        Classification.Apply(model, wall, "IfcWall", schema: "IFC 4");
        Assert.Equal("IfcWall", def.IfcType);
        Assert.Equal("Ss_25_10", def.SchemaTypes["Uniclass"]);

        Classification.Erase(def);
        Assert.Equal("", def.IfcType);
        Assert.Empty(def.SchemaTypes);
    }

    [Fact]
    public void Imported_schemas_and_the_types_they_gave_are_saved_with_the_model()
    {
        var model = new Model();
        var schema = new ClassificationSchema { Name = "Uniclass", Description = "Test schema" };
        schema.Types.Add(("Ss_25_10", ["Name", "Tag"]));
        model.Schemas.Add(schema);
        var def = new ComponentDefinition { Name = "Wall", IsGroup = true };
        def.Entities.AddFace([new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(10, 10, 0)]);
        def.SchemaTypes["Uniclass"] = "Ss_25_10";
        model.Definitions.Add(def);
        model.Entities.AddInstance(def, Transform.Identity);

        var path = Path.Combine(Path.GetTempPath(), $"dog-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(model, path);
            var back = DogFile.Load(path);
            var loaded = Assert.Single(back.Schemas);
            Assert.Equal(("Uniclass", "Test schema"), (loaded.Name, loaded.Description));
            Assert.Equal(["Name", "Tag"], Assert.Single(loaded.Types).Attributes);
            Assert.Equal("Ss_25_10", back.Definitions.Single().SchemaTypes["Uniclass"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Undo_takes_a_classification_back()
    {
        var doc = new Document(new Model());
        var def = new ComponentDefinition { Name = "Wall", IsGroup = true };
        def.Entities.AddFace([new Vec3(0, 0, 0), new Vec3(10, 0, 0), new Vec3(10, 10, 0)]);
        doc.Model.Definitions.Add(def);
        var wall = doc.Model.Entities.AddInstance(def, Transform.Identity);

        doc.Operation("Classify", _ => Classification.Apply(doc.Model, wall, "Ss_25_10", schema: "Uniclass"));
        Assert.Equal("Ss_25_10", def.SchemaTypes["Uniclass"]);
        doc.Undo.Undo();
        Assert.Empty(def.SchemaTypes);
    }
}
