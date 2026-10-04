using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class IntersectTests
{
    private static void Cube(Entities e, Vec3 o, double s)
    {
        Vec3 P(int x, int y, int z) => o + new Vec3(x * s, y * s, z * s);
        e.AddFace([P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)]);
        e.AddFace([P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)]);
        e.AddFace([P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]);
        e.AddFace([P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)]);
        e.AddFace([P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)]);
        e.AddFace([P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)]);
    }

    [Fact]
    public void Overlapping_loose_cubes_get_their_intersection_outline()
    {
        var m = new Model();
        Cube(m.Entities, Vec3.Zero, 10);
        Cube(m.Entities, new Vec3(5, 5, 5), 10);
        var doc = new Document(m);
        doc.Selection.Set(m.Entities.Faces.Cast<object>().ToList());
        var edges = Intersect.WithModel(doc);
        Assert.Equal(6, edges.Count);
        Assert.Equal(30, edges.Sum(e => e.Length), 6);
        Assert.Equal(18, m.Entities.Faces.Count); // the six crossed faces split in two
    }

    [Fact]
    public void Group_intersection_edges_go_to_the_active_context()
    {
        var m = new Model();
        var a = new ComponentDefinition { IsGroup = true };
        var b = new ComponentDefinition { IsGroup = true };
        Cube(a.Entities, Vec3.Zero, 10);
        Cube(b.Entities, Vec3.Zero, 10);
        m.Definitions.Add(a);
        m.Definitions.Add(b);
        var ia = m.Entities.AddInstance(a, Transform.Identity);
        m.Entities.AddInstance(b, Transform.Translation(new Vec3(5, 5, 5)));
        var doc = new Document(m);
        doc.Selection.Set([ia]);
        Intersect.WithModel(doc);
        Assert.Equal(6, m.Entities.Edges.Count);
        Assert.Equal(12, a.Entities.Edges.Count);
    }

    [Fact]
    public void Faces_split_whatever_order_the_new_edges_come_in()
    {
        (Vec3, Vec3)[] segments =
        [
            (new(5, 5, 10), new(10, 5, 10)), (new(5, 10, 10), new(5, 5, 10)), (new(10, 10, 5), new(5, 10, 5)),
            (new(5, 10, 5), new(5, 10, 10)), (new(10, 5, 5), new(10, 10, 5)), (new(10, 5, 10), new(10, 5, 5)),
        ];
        foreach (var order in new[] { segments, segments.Reverse().ToArray() })
        {
            var e = new Entities();
            Cube(e, Vec3.Zero, 10);
            Cube(e, new Vec3(5, 5, 5), 10);
            var edges = order.SelectMany(s => StickyGeometry.AddSegment(e, s.Item1, s.Item2)).ToList();
            FaceFinder.Update(e, edges);
            Assert.Equal(18, e.Faces.Count);
        }
    }

    [Fact]
    public void Active_section_cuts_a_cube_in_a_square()
    {
        var m = new Model();
        Cube(m.Entities, Vec3.Zero, 10);
        var plane = new SectionPlane(new Vec3(0, 0, 4), Vec3.UnitZ);
        m.Entities.SectionPlanes.Add(plane);
        Assert.Empty(Intersect.SectionCut(m));
        m.Entities.ActiveSection = plane;
        var cut = Intersect.SectionCut(m);
        Assert.Equal(4, cut.Count);
        Assert.All(cut, s => Assert.Equal(4, s.A.Z, 9));
        Assert.Equal(40, cut.Sum(s => s.A.DistanceTo(s.B)), 6);
    }
}
