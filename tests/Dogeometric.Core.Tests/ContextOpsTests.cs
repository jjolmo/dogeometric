using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ContextOpsTests
{
    [Fact]
    public void Welding_a_chain_makes_one_curve_and_exploding_undoes_it()
    {
        var e = new Entities();
        var edges = StickyGeometry.DrawEdges(e, [new(0, 0, 0), new(10, 0, 0), new(20, 5, 0), new(30, 5, 0)]);
        var curve = ContextOps.Weld(edges)!;
        Assert.All(edges, x => Assert.Same(curve, x.Curve));
        Assert.Equal(3, ContextOps.ExplodeCurves(edges));
        Assert.All(edges, x => Assert.Null(x.Curve));

        var apart = StickyGeometry.DrawEdges(e, [new(0, 50, 0), new(10, 50, 0)]).Concat(StickyGeometry.DrawEdges(e, [new(0, 60, 0), new(10, 60, 0)])).ToList();
        Assert.Null(ContextOps.Weld(apart));
    }

    [Fact]
    public void A_circles_centre_and_polygon_conversion()
    {
        var e = new Entities();
        var curve = new Curve { Center = new Vec3(5, 5, 0), Normal = Vec3.UnitZ, Radius = 10, Segments = 24 };
        var edges = StickyGeometry.DrawEdges(e, Shapes.RegularPolygon(curve.Center, Vec3.UnitZ, Vec3.UnitX, 10, 24), closed: true, Vec3.UnitZ, curve);
        Assert.Equal(new Vec3(5, 5, 0), ContextOps.FindCenter(e, edges[3])!.Position);
        Assert.Equal(1, ContextOps.ToPolygon(edges));
        Assert.True(curve.IsPolygon);
        // The 24-sided polygon drawn, not the ideal circle.
        Assert.Equal(24 * 0.5 * 100 * Math.Sin(2 * Math.PI / 24), ContextOps.Area(e.Faces), 6);
    }

    [Fact]
    public void Resetting_scale_and_skew_and_scaling_the_definition()
    {
        var def = new ComponentDefinition();
        TestModels.Box(def.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        var inst = new ComponentInstance(def) { Transform = new Transform(new Vec3(2, 0, 0), new Vec3(1, 3, 0), new Vec3(0, 0, 0.5), new Vec3(7, 0, 0)) };
        ContextOps.ResetSkew(inst);
        Assert.Equal(0, inst.Transform.X.Dot(inst.Transform.Y), 9);
        ContextOps.ResetScale(inst);
        Assert.Equal((1.0, 1.0, 1.0), (inst.Transform.X.Length, inst.Transform.Y.Length, inst.Transform.Z.Length));
        Assert.Equal(new Vec3(7, 0, 0), inst.Transform.Origin);

        var scaled = new ComponentInstance(def) { Transform = new Transform(new Vec3(2, 0, 0), new Vec3(0, 2, 0), new Vec3(0, 0, 2), Vec3.Zero) };
        ContextOps.ScaleDefinition(scaled);
        Assert.Equal(1, scaled.Transform.X.Length, 9);
        Assert.Equal(20, def.Entities.Vertices.Max(v => v.Position.X), 9);
    }

    [Fact]
    public void A_circle_and_an_arc_redraw_with_new_segments_and_radius()
    {
        var e = new Entities();
        var circle = new Curve { Center = Vec3.Zero, Normal = Vec3.UnitZ, Radius = 10, Segments = 24 };
        StickyGeometry.DrawEdges(e, Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 10, 24), closed: true, Vec3.UnitZ, circle);
        var edges = Curves.Redraw(e, circle, 12, 15)!;
        Assert.Equal(12, edges.Count);
        Assert.All(edges, x => Assert.Equal(15, x.Start.Position.Length, 9));
        Assert.Single(e.Faces);

        var arc = new Curve { Center = new Vec3(50, 0, 0), Normal = Vec3.UnitZ, Radius = 10, Segments = 24 };
        var pts = Shapes.CenterArc(arc.Center, Vec3.UnitZ, new Vec3(60, 0, 0), -Math.PI / 2, 6);
        StickyGeometry.DrawEdges(e, pts, closed: false, Vec3.UnitZ, arc);
        var redrawn = Curves.Redraw(e, arc, 48, 10)!;
        Assert.Equal(12, redrawn.Count);
        var ends = Curves.Points(e, redrawn[0].Curve!, out var closed);
        Assert.False(closed);
        Assert.Contains(ends, p => p.DistanceTo(new Vec3(60, 0, 0)) < 1e-9);
        Assert.Contains(ends, p => p.DistanceTo(new Vec3(50, -10, 0)) < 1e-9);
    }

    [Fact]
    public void Double_clicking_a_circle_picks_all_its_edges_and_its_face()
    {
        var e = new Entities();
        var circle = new Curve { Center = Vec3.Zero, Normal = Vec3.UnitZ, Radius = 10, Segments = 24 };
        var edges = StickyGeometry.DrawEdges(e, Shapes.RegularPolygon(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 10, 24), closed: true, Vec3.UnitZ, circle);
        var set = Topology.DoubleClickSet(e, edges[5]).ToList();
        Assert.Equal(25, set.Count);
        Assert.Contains(e.Faces[0], set);
    }
}
