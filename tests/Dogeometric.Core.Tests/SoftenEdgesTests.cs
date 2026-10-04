using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SoftenEdgesTests
{
    [Fact]
    public void Edges_below_the_angle_soften_and_steeper_ones_harden_again()
    {
        var e = new Entities();
        // A 24-sided prism: sides meet at 15 degrees, sides and caps at 90.
        var ring = Enumerable.Range(0, 24).Select(i => new Vec3(10 * Math.Cos(i * Math.PI / 12), 10 * Math.Sin(i * Math.PI / 12), 0)).ToList();
        e.AddFace(ring.Select(p => p + new Vec3(0, 0, 20)).ToList());
        e.AddFace(Enumerable.Reverse(ring).ToList());
        for (var i = 0; i < 24; i++)
        {
            var j = (i + 1) % 24;
            e.AddFace([ring[i], ring[j], ring[j] + new Vec3(0, 0, 20), ring[i] + new Vec3(0, 0, 20)]);
        }
        Assert.Equal(24, Editing.SoftenEdges(e, e.Edges, 20, smoothNormals: true, softenCoplanar: false));
        Assert.All(e.Edges.Where(x => x.Start.Position.Z != x.End.Position.Z), x => Assert.True(x.Flags.HasFlag(EdgeFlags.Smooth)));
        Assert.Equal(0, Editing.SoftenEdges(e, e.Edges, 10, smoothNormals: true, softenCoplanar: false));
        Assert.All(e.Edges, x => Assert.False(x.Flags.HasFlag(EdgeFlags.Soft)));
    }

    [Fact]
    public void Coplanar_edges_need_soften_coplanar()
    {
        var e = new Entities();
        e.AddFace([new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0)]);
        e.AddFace([new(10, 0, 0), new(20, 0, 0), new(20, 10, 0), new(10, 10, 0)]);
        var seam = e.Edges.Single(x => Topology.FacesOf(e, x).Count() == 2);
        Assert.Equal(0, Editing.SoftenEdges(e, [seam], 45, true, softenCoplanar: false));
        Assert.Equal(1, Editing.SoftenEdges(e, [seam], 45, true, softenCoplanar: true));
    }
}
