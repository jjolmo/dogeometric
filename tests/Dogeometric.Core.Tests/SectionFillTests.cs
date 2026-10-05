using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SectionFillTests
{
    /// <summary>An axis-aligned box from min to max, its faces turned outwards (or inwards for a cavity).</summary>
    internal static void Box(Entities e, Vec3 min, Vec3 max, bool inwards = false)
    {
        var w = new Welder(e);
        Vec3 P(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        List<Vec3>[] quads =
        [
            [P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)],
            [P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)],
            [P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)],
            [P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)],
            [P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)],
            [P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)],
        ];
        foreach (var q in quads)
        {
            var f = w.Face(q, []);
            var outward = Polygon.Normal(q);
            if ((f.Normal.Dot(outward) < 0) != inwards)
                FaceFinder.Reverse(f);
        }
    }

    internal static Model HollowBox()
    {
        var m = new Model();
        Box(m.Entities, Vec3.Zero, new Vec3(40, 30, 20));
        Box(m.Entities, new Vec3(0.5, 0.5, 0.5), new Vec3(39.5, 29.5, 19.5), inwards: true);
        var plane = new SectionPlane(new Vec3(20, 15, 10), new Vec3(0, 1, 0));
        m.Entities.SectionPlanes.Add(plane);
        m.Entities.ActiveSection = plane;
        return m;
    }

    private static double Area(List<Vec3> tris, Vec3 normal)
    {
        var a = 0.0;
        for (var i = 0; i < tris.Count; i += 3)
            a += Math.Abs((tris[i + 1] - tris[i]).Cross(tris[i + 2] - tris[i]).Dot(normal)) / 2;
        return a;
    }

    [Fact]
    public void A_hollow_box_cut_fills_only_its_half_millimetre_wall()
    {
        var m = HollowBox();
        Assert.Equal(2, SectionFill.Loops(Intersect.SectionCut(m)).Count);
        Assert.Equal(40 * 20 - 39 * 19, Area(SectionFill.Triangles(m), Vec3.UnitY), 6);
    }

    [Fact]
    public void Nested_parts_fill_as_rings_and_islands()
    {
        var m = HollowBox();
        Box(m.Entities, new Vec3(10, 5, 5), new Vec3(20, 25, 10));
        Assert.Equal(59 + 10 * 5, Area(SectionFill.Triangles(m), Vec3.UnitY), 6);
    }

    [Fact]
    public void The_fill_setting_is_saved_and_undone()
    {
        var doc = new Document(HollowBox());
        doc.Undo.Begin("Section Fill");
        doc.Model.ShowSectionFill = false;
        doc.Undo.Commit();
        var path = Path.Combine(Path.GetTempPath(), $"fill-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(doc.Model, path);
            Assert.False(DogFile.Load(path).ShowSectionFill);
        }
        finally
        {
            File.Delete(path);
        }
        doc.Undo.Undo();
        Assert.True(doc.Model.ShowSectionFill);
    }

    [Fact]
    public void The_section_slice_exports_flat_at_full_scale()
    {
        var dxf = HiddenLine.SectionSliceDxf(HollowBox())!;
        var e = DxfImport.Read(dxf).Definitions[0].Entities;
        Assert.Equal(new Vec3(40, 20, 0), e.Bounds().Size);
        var ring = e.Faces.Single(f => f.Loops.Count == 2);
        Assert.Equal(40 * 20 - 39 * 19, ring.Area, 6);
        Assert.Null(HiddenLine.SectionSliceDxf(new Model()));
    }
}
