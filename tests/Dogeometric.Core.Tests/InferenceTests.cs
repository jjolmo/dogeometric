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

    /// <summary>A plan view where the cursor picks <paramref name="edge"/> when within a unit of it.</summary>
    private sealed class PlanViewOver(Edge edge) : IViewProjection
    {
        public Ray RayAt(double x, double y) => new(new Vec3(x / 10, -y / 10, 1000), -Vec3.UnitZ);
        public (double X, double Y)? ToScreen(Vec3 p) => (p.X * 10, -p.Y * 10);
        public Vec3 ViewDirection => -Vec3.UnitZ;

        public PickHit? Pick(double x, double y)
        {
            var q = new Vec3(x / 10, -y / 10, 0);
            var (a, b) = (edge.Start.Position, edge.End.Position);
            var t = Math.Clamp((q - a).Dot(b - a) / (b - a).Dot(b - a), 0, 1);
            var on = a + (b - a) * t;
            return on.DistanceTo(q) < 1 ? new PickHit(edge, [], on, 1000) : null;
        }
    }

    [Fact]
    public void Lines_infer_parallel_and_perpendicular_to_an_edge_crossed_on_the_way()
    {
        var e = new Entities();
        var edge = StickyGeometry.DrawEdges(e, [new(0, 0, 0), new(10, 5, 0)])[0];
        var view = new PlanViewOver(edge);
        var engine = new InferenceEngine();
        var from = new Vec3(30, 0, 0);
        InferenceResult At(Vec3 p) => engine.Infer(view, p.X * 10, -p.Y * 10, from, e, Transform.Identity);

        Assert.NotEqual("Parallel to Edge", At(from + new Vec3(20, 10.3, 0)).Label);
        At(new Vec3(4, 2, 0));
        var parallel = At(from + new Vec3(20, 10.3, 0));
        Assert.Equal("Parallel to Edge", parallel.Label);
        Assert.True((parallel.Point - from).Normalized().Dot(new Vec3(10, 5, 0).Normalized()) > 1 - 1e-9);

        var perpendicular = At(from + new Vec3(5.3, -10, 0));
        Assert.Equal("Perpendicular to Edge", perpendicular.Label);
        Assert.Equal(0, (perpendicular.Point - from).Dot(new Vec3(10, 5, 0)), 9);
        // Starting a new line forgets the edge.
        engine.Infer(view, 0, 0, null, e, Transform.Identity);
        Assert.NotEqual("Parallel to Edge", At(from + new Vec3(20, 10.3, 0)).Label);
    }

    [Fact]
    public void Length_snapping_rounds_free_points_but_not_snapped_ones()
    {
        var from = new Vec3(1, 1, 0);
        var free = new InferenceResult(new Vec3(1 + 12.4, 1, 0), InferenceKind.OnAxis, "On Red Axis", from, Vec3.UnitX);
        Assert.Equal(new Vec3(1 + 12.5, 1, 0), InferenceEngine.SnapLength(free, from, 2.5).Point);
        var endpoint = new InferenceResult(new Vec3(13.4, 1, 0), InferenceKind.Endpoint, "Endpoint");
        Assert.Same(endpoint, InferenceEngine.SnapLength(endpoint, from, 2.5));
    }

    [Fact]
    public void Linear_inferences_can_be_limited_or_turned_off()
    {
        var e = new Entities();
        var edge = StickyGeometry.DrawEdges(e, [new(0, 0, 0), new(10, 5, 0)])[0];
        var view = new PlanViewOver(edge);
        var engine = new InferenceEngine();
        var from = new Vec3(30, 0, 0);
        InferenceResult At(Vec3 p) => engine.Infer(view, p.X * 10, -p.Y * 10, from, e, Transform.Identity);

        Assert.Equal("On Red Axis", At(from + new Vec3(20, 0.2, 0)).Label);
        engine.Linear = LinearInferences.ParallelPerpendicularOnly;
        Assert.NotEqual("On Red Axis", At(from + new Vec3(20, 0.2, 0)).Label);
        At(new Vec3(4, 2, 0));
        Assert.Equal("Parallel to Edge", At(from + new Vec3(20, 10.3, 0)).Label);
        engine.Linear = LinearInferences.AllOff;
        Assert.NotEqual("Parallel to Edge", At(from + new Vec3(20, 10.3, 0)).Label);
    }

    /// <summary>A perspective-like view from above and in front, as the default camera looks: a pixel (x, y) is the
    /// ground point (x, y, 0) seen from the eye.</summary>
    private sealed class ObliqueView : IViewProjection
    {
        private static readonly Vec3 Eye = new(0, -5000, 2000);
        public Ray RayAt(double x, double y) => new(Eye, (new Vec3(x, y, 0) - Eye).Normalized());
        public (double X, double Y)? ToScreen(Vec3 p) => (p.X, p.Y);
        public PickHit? Pick(double x, double y) => null;
        public Vec3 ViewDirection => (Vec3.Zero - Eye).Normalized();
    }

    [Fact]
    public void Free_points_after_the_first_stay_on_the_ground_in_an_ordinary_view()
    {
        // From a point on the ground, with nothing under the cursor, SketchUp keeps drawing on the ground.
        var r = new InferenceEngine().Infer(new ObliqueView(), 300, 400, Vec3.Zero, new Entities(), Transform.Identity);
        Assert.Equal(0, r.Point.Z, 6);
        Assert.Equal(300, r.Point.X, 6);
        Assert.Equal(400, r.Point.Y, 6);
    }

    [Fact]
    public void Only_a_level_view_draws_on_the_plane_facing_it()
    {
        Assert.Equal(Vec3.UnitZ, InferenceEngine.DrawingPlane(new Vec3(0, 0.93, -0.37), Transform.Identity));
        Assert.Equal(Vec3.UnitY, InferenceEngine.DrawingPlane(new Vec3(0, 1, -0.02), Transform.Identity));
    }
}
