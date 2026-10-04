using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ExportTests
{
    private static List<Triangle> ReadBinaryStl(byte[] data)
    {
        Assert.True(data.Length >= 84);
        var count = BitConverter.ToUInt32(data, 80);
        Assert.Equal(84 + count * 50, (uint)data.Length);
        var tris = new List<Triangle>();
        for (var i = 0; i < count; i++)
        {
            var o = 84 + i * 50 + 12; // skip normal
            Vec3 V(int k) => new(BitConverter.ToSingle(data, o + k * 12), BitConverter.ToSingle(data, o + k * 12 + 4), BitConverter.ToSingle(data, o + k * 12 + 8));
            tris.Add(new Triangle(V(0), V(1), V(2), null));
        }
        return tris;
    }

    private static byte[] Stl(Model model, ExportOptions? options = null)
    {
        using var ms = new MemoryStream();
        StlWriter.WriteBinary(MeshExtractor.Extract(model, options), ms);
        return ms.ToArray();
    }

    [Fact]
    public void Stl_of_closed_box_is_watertight_with_outward_normals()
    {
        var model = new Model();
        TestModels.Box(model.Entities, new Vec3(1, 2, 3), new Vec3(40, 30, 20));
        var report = MeshCheck.Analyze(ReadBinaryStl(Stl(model)));

        Assert.Equal(12, report.Triangles);
        Assert.True(report.IsWatertight, report.ToString());
        Assert.Equal(40 * 30 * 20, report.Volume, 3); // positive: faces point out
    }

    [Fact]
    public void Stl_exports_whole_model_or_only_the_selection()
    {
        var (model, a, b) = TestModels.TwoBoxGroups();

        var all = MeshCheck.Analyze(ReadBinaryStl(Stl(model)));
        Assert.Equal(24, all.Triangles);
        Assert.Equal(10 * 20 * 30 + 125, all.Volume, 3);

        var onlyB = MeshCheck.Analyze(ReadBinaryStl(Stl(model, new ExportOptions { Selection = new HashSet<object> { b } })));
        Assert.Equal(12, onlyB.Triangles);
        Assert.True(onlyB.IsWatertight);
        Assert.Equal(125, onlyB.Volume, 3);
    }

    [Fact]
    public void Hidden_entities_are_skipped_unless_asked()
    {
        var (model, _, b) = TestModels.TwoBoxGroups();
        b.Hidden = true;
        Assert.Equal(12, MeshExtractor.Extract(model).Count);
        Assert.Equal(24, MeshExtractor.Extract(model, new ExportOptions { IncludeHidden = true }).Count);

        b.Hidden = false;
        b.Tag = model.GetOrAddTag("Hidden tag");
        b.Tag.Visible = false;
        Assert.Equal(12, MeshExtractor.Extract(model).Count);
    }

    [Fact]
    public void Mirrored_instances_keep_outward_normals()
    {
        var (model, _, b) = TestModels.TwoBoxGroups();
        b.Transform = Transform.Scaling(-1, 1, 1).Then(Transform.Translation(new Vec3(100, 0, 0)));
        var onlyB = MeshCheck.Analyze(MeshExtractor.Extract(model, new ExportOptions { Selection = new HashSet<object> { b } }));
        Assert.True(onlyB.IsWatertight);
        Assert.Equal(125, onlyB.Volume, 3);
    }

    [Fact]
    public void Open_mesh_is_not_watertight()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        model.Entities.Faces.RemoveAt(0);
        var report = MeshCheck.Analyze(MeshExtractor.Extract(model));
        Assert.False(report.IsWatertight);
        Assert.Equal(4, report.BoundaryEdges);
    }

    [Fact]
    public void Ascii_stl_has_one_facet_per_triangle()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        var sw = new StringWriter();
        StlWriter.WriteAscii(MeshExtractor.Extract(model), sw);
        var text = sw.ToString();
        Assert.StartsWith("solid ", text);
        Assert.Equal(12, text.Split("facet normal").Length - 1);
        Assert.EndsWith("endsolid Dogeometric" + Environment.NewLine, text);
    }

    [Fact]
    public void Obj_welds_vertices_and_writes_materials()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        var red = new Material { Name = "Red paint", Color = new Rgba(255, 0, 0) };
        model.Materials.Add(red);
        model.Entities.Faces[0].FrontMaterial = red;

        var dir = Directory.CreateTempSubdirectory("dogeometric-obj").FullName;
        var objPath = Path.Combine(dir, "box.obj");
        ObjWriter.Write(MeshExtractor.Extract(model), objPath);
        var lines = File.ReadAllLines(objPath);

        Assert.Equal(8, lines.Count(l => l.StartsWith("v ")));
        Assert.Equal(12, lines.Count(l => l.StartsWith("f ")));
        Assert.Contains("mtllib box.mtl", lines);
        var mtl = File.ReadAllText(Path.Combine(dir, "box.mtl"));
        Assert.Contains("newmtl Red_paint", mtl);
        Assert.Contains("Kd 1 0 0", mtl);
        // Every face index refers to an existing vertex.
        Assert.All(lines.Where(l => l.StartsWith("f ")), l => Assert.All(l[2..].Split(' '), i => Assert.InRange(int.Parse(i, CultureInfo.InvariantCulture), 1, 8)));
    }

    [Fact]
    public void Glb_is_structurally_valid()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(1000, 2000, 3000));
        using var ms = new MemoryStream();
        GltfWriter.WriteGlb(MeshExtractor.Extract(model), ms);
        var data = ms.ToArray();

        Assert.Equal(0x46546C67u, BitConverter.ToUInt32(data, 0));
        Assert.Equal(2u, BitConverter.ToUInt32(data, 4));
        Assert.Equal((uint)data.Length, BitConverter.ToUInt32(data, 8));
        var jsonLength = (int)BitConverter.ToUInt32(data, 12);
        Assert.Equal(0x4E4F534Au, BitConverter.ToUInt32(data, 16));
        Assert.Equal(0, jsonLength % 4);
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(data, 20, jsonLength));
        var binLength = (int)BitConverter.ToUInt32(data, 20 + jsonLength);
        Assert.Equal(0x004E4942u, BitConverter.ToUInt32(data, 24 + jsonLength));
        Assert.Equal(binLength, json.RootElement.GetProperty("buffers")[0].GetProperty("byteLength").GetInt32() + (4 - binLength % 4) % 4);

        var root = json.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        var pos = root.GetProperty("accessors")[0];
        Assert.Equal(36, pos.GetProperty("count").GetInt32());
        // Y-up metres: model Z (3000 mm) becomes glTF Y = 3 m; model Y becomes -Z.
        var max = pos.GetProperty("max").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var min = pos.GetProperty("min").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        Assert.Equal(1, max[0], 5);
        Assert.Equal(3, max[1], 5);
        Assert.Equal(-2, min[2], 5);
        foreach (var view in root.GetProperty("bufferViews").EnumerateArray())
            Assert.True(view.GetProperty("byteOffset").GetInt32() + view.GetProperty("byteLength").GetInt32() <= binLength);
    }

    [Fact]
    public void Empty_glb_is_still_valid()
    {
        using var ms = new MemoryStream();
        GltfWriter.WriteGlb([], ms);
        var data = ms.ToArray();
        Assert.Equal((uint)data.Length, BitConverter.ToUInt32(data, 8));
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(data, 20, (int)BitConverter.ToUInt32(data, 12)));
        Assert.False(json.RootElement.TryGetProperty("meshes", out _));
    }

    [Fact]
    public void Dae_declares_millimetres_z_up_and_all_triangles()
    {
        var model = new Model();
        TestModels.Box(model.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        using var ms = new MemoryStream();
        DaeWriter.Write(MeshExtractor.Extract(model), ms);
        ms.Position = 0;
        var doc = XDocument.Load(ms);
        XNamespace ns = "http://www.collada.org/2005/11/COLLADASchema";

        Assert.Equal("1.4.1", doc.Root!.Attribute("version")!.Value);
        Assert.Equal("0.001", doc.Descendants(ns + "unit").Single().Attribute("meter")!.Value);
        Assert.Equal("Z_UP", doc.Descendants(ns + "up_axis").Single().Value);
        var tris = doc.Descendants(ns + "triangles").Single();
        Assert.Equal("12", tris.Attribute("count")!.Value);
        Assert.Equal(12 * 3 * 2, tris.Element(ns + "p")!.Value.Split(' ').Length);
        Assert.Equal(8 * 3, doc.Descendants(ns + "float_array").First().Value.Split(' ').Length);
    }
}
