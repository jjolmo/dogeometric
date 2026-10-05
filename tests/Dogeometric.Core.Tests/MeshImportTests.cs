using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class MeshImportTests
{
    private static MeshCheck.Report Check(Model m) => MeshCheck.Analyze(MeshExtractor.Extract(m));

    private static byte[] BinaryStl(Model m)
    {
        using var s = new MemoryStream();
        StlWriter.WriteBinary(MeshExtractor.Extract(m), s);
        return s.ToArray();
    }

    [Fact]
    public void A_box_comes_back_from_stl_with_six_faces()
    {
        var box = new Model();
        TestModels.Box(box.Entities, Vec3.Zero, new Vec3(80, 50, 30));
        var m = MeshImport.Build("box", MeshImport.ReadStl(BinaryStl(box)));
        var e = Assert.Single(m.Definitions).Entities;
        Assert.Equal(6, e.Faces.Count);
        Assert.Equal(12, e.Edges.Count);
        Assert.Empty(SolidInspector.Find(e));
        Assert.Equal(80 * 50 * 30, Check(m).Volume, 6);
    }

    [Fact]
    public void A_hollow_box_keeps_its_rim_as_one_ring_face()
    {
        var box = new Model();
        SectionFillTests.Box(box.Entities, Vec3.Zero, new Vec3(40, 30, 20));
        SectionFillTests.Box(box.Entities, new Vec3(2, 2, 2), new Vec3(38, 28, 18), inwards: true);
        var m = MeshImport.Build("hollow", MeshImport.ReadStl(BinaryStl(box)));
        var e = m.Definitions[0].Entities;
        Assert.Equal(12, e.Faces.Count);
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Ascii_stl_and_obj_read_the_same_square()
    {
        var ascii = """
            solid s
            facet normal 0 0 1
            outer loop
            vertex 0 0 0
            vertex 10 0 0
            vertex 10 10 0
            endloop
            endfacet
            facet normal 0 0 1
            outer loop
            vertex 0 0 0
            vertex 10 10 0
            vertex 0 10 0
            endloop
            endfacet
            endsolid s
            """;
        var fromStl = MeshImport.Build("s", MeshImport.ReadStl(Encoding.ASCII.GetBytes(ascii)));
        Assert.Single(fromStl.Definitions[0].Entities.Faces);
        var obj = "v 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf 1 2 3 4\n";
        var fromObj = MeshImport.Build("o", MeshImport.ReadObj(obj), mmPerUnit: 10);
        var face = Assert.Single(fromObj.Definitions[0].Entities.Faces);
        Assert.Equal(100, face.Area, 9);
    }

    [Fact]
    public void A_fine_sphere_imports_quickly_and_closed()
    {
        var sphere = new Model();
        Sphere.Add(sphere.Entities, Vec3.Zero, 20, 96);
        var tris = MeshImport.ReadStl(BinaryStl(sphere));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var m = MeshImport.Build("sphere", tris);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Empty(SolidInspector.Find(m.Definitions[0].Entities));
    }
}
