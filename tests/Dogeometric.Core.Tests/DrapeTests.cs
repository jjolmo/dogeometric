using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class DrapeTests
{
    [Fact]
    public void A_rectangle_dropped_on_a_terrain_follows_it_and_splits_its_faces()
    {
        var e = new Entities();
        Sandbox.Grid(e, Vec3.Zero, Vec3.UnitX, 100, Vec3.UnitY, 100, 10);
        Sandbox.Smoove(e, new Vec3(50, 50, 0), 40, Vec3.UnitZ, 25);
        var before = e.Faces.Count;
        var runs = Sandbox.DrapePath(e, [new(20, 20, 100), new(80, 25, 100), new(75, 80, 100), new(25, 75, 100)], -Vec3.UnitZ, closed: true);
        var run = Assert.Single(runs);
        Assert.True(run.Count > 20);
        foreach (var r in runs)
            StickyGeometry.DrawEdges(e, r);
        Assert.True(e.Faces.Count > before);
        Assert.Equal(40, e.Edges.Count(x => Topology.FacesOf(e, x).Count() == 1));
        Assert.DoesNotContain(e.Edges, x => Topology.FacesOf(e, x).Count() > 2);
    }

    [Fact]
    public void A_path_off_the_surface_is_cut_where_it_leaves()
    {
        var e = new Entities();
        e.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        var runs = Sandbox.DrapePath(e, [new(5, 5, 50), new(30, 5, 50)], -Vec3.UnitZ, closed: false);
        // The part over the face lies on it; the part beyond is dropped.
        Assert.Single(runs);
        Assert.All(runs[0], p => Assert.Equal(0, p.Z, 9));
    }
}
