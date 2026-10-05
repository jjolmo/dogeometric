using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;

namespace Dogeometric.Core.Tests;

public class InferenceTests
{
    /// <summary>A plan view, 10 pixels per millimetre, looking down.</summary>
    private sealed class PlanView : IViewProjection
    {
        public Ray RayAt(double x, double y) => new(new Vec3(x / 10, -y / 10, 1000), -Vec3.UnitZ);
        public (double X, double Y)? ToScreen(Vec3 p) => (p.X * 10, -p.Y * 10);
        public PickHit? Pick(double x, double y) => null;
        public Vec3 ViewDirection => -Vec3.UnitZ;
    }

    private static InferenceResult At(Entities e, Vec3 p) =>
        new InferenceEngine().Infer(new PlanView(), p.X * 10, -p.Y * 10, null, e, Transform.Identity);

    [Fact]
    public void Arcs_offer_their_ends_but_not_each_segments_points()
    {
        var e = new Entities();
        var centre = new Vec3(0, 0, 0);
        var curve = new Curve { Center = centre, Normal = Vec3.UnitZ, Radius = 10, Segments = 24 };
        var pts = Shapes.CenterArc(centre, Vec3.UnitZ, new Vec3(10, 0, 0), Math.PI, 12);
        StickyGeometry.DrawEdges(e, pts, closed: false, Vec3.UnitZ, curve);
        Assert.Equal(InferenceKind.Endpoint, At(e, pts[0]).Kind);
        Assert.Equal(InferenceKind.Endpoint, At(e, pts[^1]).Kind);
        Assert.NotEqual(InferenceKind.Endpoint, At(e, pts[5]).Kind);
        Assert.NotEqual(InferenceKind.Midpoint, At(e, (pts[5] + pts[6]) * 0.5).Kind);
    }

    [Fact]
    public void Straight_edges_and_polygons_keep_their_midpoints()
    {
        var e = new Entities();
        StickyGeometry.DrawEdges(e, [Vec3.Zero, new Vec3(10, 0, 0)]);
        Assert.Equal(InferenceKind.Midpoint, At(e, new Vec3(5, 0, 0)).Kind);
        var poly = new Entities();
        var curve = new Curve { Center = Vec3.Zero, Normal = Vec3.UnitZ, Radius = 10, Segments = 6, IsPolygon = true };
        var pts = Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 10, 6);
        StickyGeometry.DrawEdges(poly, pts, closed: true, Vec3.UnitZ, curve);
        Assert.Equal(InferenceKind.Endpoint, At(poly, pts[2]).Kind);
    }
}
