using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class FilletTests
{
    [Fact]
    public void Rounding_a_square_corner_keeps_one_face_with_an_arc()
    {
        var e = new Entities();
        e.AddFace([new Vec3(0, 0, 0), new Vec3(100, 0, 0), new Vec3(100, 100, 0), new Vec3(0, 100, 0)]);
        var corner = e.Vertices.Single(v => v.Position == new Vec3(100, 100, 0));
        Assert.True(Fillet.Apply(e, corner, 20, 6));

        Assert.DoesNotContain(corner, e.Vertices);
        var face = Assert.Single(e.Faces);
        // Two whole sides, the two trimmed ones and 6 arc segments.
        Assert.Equal(10, face.OuterLoop.Edges.Count);
        // The corner square loses what the 6-segment quarter circle doesn't cover.
        var sector = 6 * 0.5 * 20 * 20 * Math.Sin(Math.PI / 12);
        Assert.Equal(100 * 100 - (20 * 20 - sector), face.Area, 6);
        Assert.Contains(e.Vertices, v => v.Position.DistanceTo(new Vec3(80, 100, 0)) < 1e-6);
        Assert.Contains(e.Vertices, v => v.Position.DistanceTo(new Vec3(100, 80, 0)) < 1e-6);
    }

    [Fact]
    public void A_radius_too_big_for_the_edges_or_a_straight_vertex_is_refused()
    {
        var e = new Entities();
        e.AddFace([new Vec3(0, 0, 0), new Vec3(100, 0, 0), new Vec3(100, 100, 0), new Vec3(0, 100, 0)]);
        var corner = e.Vertices.Single(v => v.Position == new Vec3(100, 100, 0));
        Assert.False(Fillet.Apply(e, corner, 150, 6));
        Assert.Equal(4, e.Edges.Count);
        Assert.Equal(20, Fillet.RadiusFor(Vec3.UnitX, Vec3.UnitY, 20), 9);
    }
}
