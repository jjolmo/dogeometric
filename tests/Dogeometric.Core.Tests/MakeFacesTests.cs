using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class MakeFacesTests
{
    private static void Loop(Entities e, params Vec3[] pts)
    {
        for (var i = 0; i < pts.Length; i++)
            e.AddEdge(e.VertexAt(pts[i]), e.VertexAt(pts[(i + 1) % pts.Length]));
    }

    [Fact]
    public void A_wire_box_becomes_a_solid_facing_out()
    {
        var e = new Entities();
        Vec3[] c = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0), new(0, 0, 10), new(10, 0, 10), new(10, 10, 10), new(0, 10, 10)];
        foreach (var (a, b) in new[] { (0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4), (0, 4), (1, 5), (2, 6), (3, 7) })
            e.AddEdge(e.VertexAt(c[a]), e.VertexAt(c[b]));
        Assert.Equal(6, MakeFaces.Run(e));
        Assert.Empty(SolidInspector.Find(e));
    }

    [Fact]
    public void Nested_loops_make_a_plate_with_a_hole_and_the_hole_face()
    {
        var e = new Entities();
        Loop(e, new(0, 0, 0), new(30, 0, 0), new(30, 20, 0), new(0, 20, 0));
        Loop(e, new(10, 5, 0), new(20, 5, 0), new(20, 15, 0), new(10, 15, 0));
        Assert.Equal(2, MakeFaces.Run(e));
        Assert.Equal([100.0, 500.0], e.Faces.Select(f => Math.Round(f.Area, 6)).Order());
        Assert.Equal(0, MakeFaces.Run(e));
    }

    [Fact]
    public void Open_chains_make_nothing()
    {
        var e = new Entities();
        e.AddEdge(e.VertexAt(new Vec3(0, 0, 0)), e.VertexAt(new Vec3(10, 0, 0)));
        e.AddEdge(e.VertexAt(new Vec3(10, 0, 0)), e.VertexAt(new Vec3(10, 10, 0)));
        Assert.Equal(0, MakeFaces.Run(e));
    }
}
