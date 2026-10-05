using System.Xml.Linq;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class DaeImportTests
{
    [Fact]
    public void Our_own_export_reads_back_with_its_volume_faces_and_colours()
    {
        var (model, _, b) = TestModels.TwoBoxGroups();
        b.Material = new Material { Name = "Red", Color = new Rgba(200, 30, 30) };
        using var stream = new MemoryStream();
        DaeWriter.Write(MeshExtractor.Extract(model), stream);
        stream.Position = 0;

        var back = DaeImport.Read(XDocument.Load(stream), "boxes");

        var faces = back.AllEntities.SelectMany(e => e.Faces).ToList();
        Assert.Equal(12, faces.Count);
        var check = MeshCheck.Analyze(MeshExtractor.Extract(back));
        Assert.True(check.IsWatertight);
        Assert.Equal(10 * 20 * 30 + 125, check.Volume, 3);
        Assert.Equal(6, faces.Count(f => f.FrontMaterial?.Color == new Rgba(200, 30, 30)));
    }

    [Fact]
    public void Units_up_axis_node_transforms_and_polylists_are_applied()
    {
        // A 1×1 inch square in the XY plane of a Y-up file, raised 2 inches by its node.
        const string dae = """
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <asset><unit meter="0.0254"/><up_axis>Y_UP</up_axis></asset>
              <library_geometries><geometry id="g"><mesh>
                <source id="p"><float_array count="12">0 0 0 1 0 0 1 1 0 0 1 0</float_array>
                  <technique_common><accessor source="#pa" count="4" stride="3"/></technique_common></source>
                <vertices id="v"><input semantic="POSITION" source="#p"/></vertices>
                <polylist count="1"><input semantic="VERTEX" source="#v" offset="0"/><vcount>4</vcount><p>0 1 2 3</p></polylist>
              </mesh></geometry></library_geometries>
              <library_visual_scenes><visual_scene id="s"><node><translate>0 2 0</translate><instance_geometry url="#g"/></node></visual_scene></library_visual_scenes>
              <scene><instance_visual_scene url="#s"/></scene>
            </COLLADA>
            """;
        var model = DaeImport.Read(XDocument.Parse(dae), "square");

        Assert.Single(model.AllEntities.SelectMany(e => e.Faces));
        var points = MeshExtractor.Extract(model).SelectMany(t => new[] { t.A, t.B, t.C }).ToList();
        // Y-up's y (raised 2 inches) becomes z and its z becomes -y; inches become millimetres.
        Assert.All(points, p => Assert.Equal(0, p.Y, 6));
        Assert.Equal(25.4 * 25.4, Polygon.Area(points), 3);
        Assert.Contains(points, p => p.DistanceTo(new Vec3(0, 0, 50.8)) < 1e-6);
        Assert.Contains(points, p => p.DistanceTo(new Vec3(25.4, 0, 76.2)) < 1e-6);
    }

    [Fact]
    public void Back_sides_written_as_reversed_triangles_become_back_materials()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        var blue = new Material { Name = "Blue", Color = new Rgba(0, 0, 255) };
        var front = MeshExtractor.Extract(model);
        var back = front.Select(t => new Triangle(t.A, t.C, t.B, blue)).ToList();
        using var stream = new MemoryStream();
        DaeWriter.Write([.. front, .. back], stream);
        stream.Position = 0;

        var faces = DaeImport.Read(XDocument.Load(stream), "box").AllEntities.SelectMany(e => e.Faces).ToList();

        Assert.Equal(6, faces.Count);
        Assert.All(faces, f => Assert.Equal(new Rgba(0, 0, 255), f.BackMaterial?.Color));
    }

    [Fact]
    public void A_kmz_opens_the_model_its_kml_links_to()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        var path = Path.Combine(Path.GetTempPath(), $"dog-{Guid.NewGuid():N}.kmz");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var kml = new StreamWriter(zip.CreateEntry("doc.kml").Open()))
                kml.Write("<kml xmlns=\"http://www.opengis.net/kml/2.2\"><Placemark><Model><Link><href>models/boxes.dae</href></Link></Model></Placemark></kml>");
            using var dae = zip.CreateEntry("models/boxes.dae").Open();
            DaeWriter.Write(MeshExtractor.Extract(model), dae);
        }

        var back = DaeImport.Load(path);
        File.Delete(path);

        Assert.Equal(10 * 20 * 30 + 125, MeshCheck.Analyze(MeshExtractor.Extract(back)).Volume, 3);
    }

    [Fact]
    public void Our_kmz_export_places_the_model_at_its_location_and_reads_back()
    {
        var (model, _, _) = TestModels.TwoBoxGroups();
        model.Shadows = model.Shadows with { Latitude = 41.383, Longitude = 2.183, NorthAngle = 30 };
        var path = Path.Combine(Path.GetTempPath(), $"dog-{Guid.NewGuid():N}.kmz");
        KmzWriter.Write(model, MeshExtractor.Extract(model), path, "Two boxes");

        string kml;
        using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
        using (var reader = new StreamReader(zip.GetEntry("doc.kml")!.Open()))
            kml = reader.ReadToEnd();
        var back = DaeImport.Load(path);
        File.Delete(path);

        Assert.Contains("<latitude>41.383</latitude>", kml);
        Assert.Contains("<heading>-30</heading>", kml);
        Assert.Contains("<href>models/Two_boxes.dae</href>", kml);
        Assert.Equal(10 * 20 * 30 + 125, MeshCheck.Analyze(MeshExtractor.Extract(back)).Volume, 3);
    }

    [Fact]
    public void Shared_nodes_come_in_as_components_placed_by_their_nodes()
    {
        // SketchUp writes a component as a node in library_nodes that each placement instances.
        const string dae = """
            <COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
              <asset><unit meter="0.001"/><up_axis>Z_UP</up_axis></asset>
              <library_geometries><geometry id="g" name="peg"><mesh>
                <source id="p"><float_array count="9">0 0 0 10 0 0 0 10 0</float_array>
                  <technique_common><accessor source="#pa" count="3" stride="3"/></technique_common></source>
                <vertices id="v"><input semantic="POSITION" source="#p"/></vertices>
                <triangles count="1"><input semantic="VERTEX" source="#v" offset="0"/><p>0 1 2</p></triangles>
              </mesh></geometry></library_geometries>
              <library_nodes><node id="peg" name="Peg"><instance_geometry url="#g"/></node></library_nodes>
              <library_visual_scenes><visual_scene id="s">
                <node name="a"><instance_node url="#peg"/></node>
                <node name="b"><translate>100 0 0</translate><instance_node url="#peg"/></node>
              </visual_scene></library_visual_scenes>
              <scene><instance_visual_scene url="#s"/></scene>
            </COLLADA>
            """;
        var model = DaeImport.Read(XDocument.Parse(dae), "pegs");

        var placed = model.Entities.Instances.Single().Definition.Entities.Instances;
        Assert.Equal(2, placed.Count);
        Assert.All(placed, i => Assert.Equal("Peg", i.Definition.Name));
        Assert.Same(placed[0].Definition, placed[1].Definition);
        Assert.False(placed[0].Definition.IsGroup);
        Assert.Equal(new Vec3(100, 0, 0), placed[1].Transform.Origin);
        Assert.Single(model.AllEntities.SelectMany(e => e.Faces));
    }
}
