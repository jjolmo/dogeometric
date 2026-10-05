using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class SplinesTests
{
    private static readonly List<Vec3> Square = [new(0, 0, 0), new(40, 0, 0), new(40, 30, 0), new(0, 30, 0)];

    [Fact]
    public void Corner_treatments_have_exact_lengths()
    {
        var arcs = Splines.Compute(SplineKind.ArcCorners, Square, 360, 3, closed: true);
        Assert.Equal(140 - 8 * 3 + 2 * Math.PI * 3, Splines.Length(arcs), 1);
        var chamfer = Splines.Compute(SplineKind.Chamfer, Square, 1, 3, closed: true);
        Assert.Equal(140 - 4 * (6 - 3 * Math.Sqrt(2)), Splines.Length(chamfer), 9);
    }

    [Fact]
    public void Dog_bones_reach_past_the_corner_by_the_cutter_overcut()
    {
        var bones = Splines.Compute(SplineKind.DogBone, Square, 360, 3, closed: true);
        var b = Bounds3.FromPoints(bones);
        var ear = 3 - 3 / Math.Sqrt(2);
        Assert.Equal(-ear, b.Min.X, 2);
        Assert.Equal(40 + ear, b.Max.X, 2);
        // Every corner keeps touching its own corner point.
        foreach (var c in Square)
            Assert.Contains(bones, p => p.DistanceTo(c) < 0.05);
    }

    [Theory]
    [InlineData(SplineKind.CatmullSpline)]
    [InlineData(SplineKind.CubicBezier)]
    [InlineData(SplineKind.FSpline)]
    [InlineData(SplineKind.Courbette)]
    public void Interpolating_curves_run_through_every_control_point(SplineKind kind)
    {
        var pts = Splines.Compute(kind, Square, Splines.Info(kind).PrecisionDefault, 0, closed: false);
        foreach (var c in Square)
            Assert.Contains(pts, p => p.DistanceTo(c) < 1e-6);
    }

    [Fact]
    public void Bezier_and_bspline_start_and_end_on_the_ends()
    {
        foreach (var kind in new[] { SplineKind.ClassicBezier, SplineKind.UniformBSpline })
        {
            var pts = Splines.Compute(kind, Square, 20, 0, closed: false);
            Assert.True(pts[0].DistanceTo(Square[0]) < 1e-9);
            Assert.True(pts[^1].DistanceTo(Square[^1]) < 1e-9);
        }
    }

    [Fact]
    public void Segmentor_and_divider_split_evenly()
    {
        var seg = Splines.Compute(SplineKind.Segmentor, Square, 1, 11, closed: false);
        Assert.Equal(12, seg.Count);
        var div = Splines.Compute(SplineKind.Divider, Square, 1, 10, closed: false);
        Assert.Equal(12, div.Count);
        for (var i = 1; i < div.Count; i++)
            Assert.True(div[i].DistanceTo(div[i - 1]) <= 10 + 1e-9);
    }

    [Fact]
    public void Editing_a_spline_redraws_it_from_its_new_control_points()
    {
        var e = new Entities();
        StickyGeometry.DrawEdges(e, [new(-10, 0, 0), new(-20, 0, 0)]);
        var data = new SplineData(SplineKind.CubicBezier, [new(0, 0, 0), new(10, 10, 0), new(20, 10, 0), new(30, 0, 0)], 7, 0, false);
        var first = Splines.Draw(e, data);
        var curve = first[0].Curve!;
        Assert.All(first, x => Assert.Same(curve, x.Curve));

        var moved = data with { ControlPoints = [new(0, 0, 0), new(10, 10, 0), new(20, 10, 0), new(40, 5, 0)] };
        var second = Splines.Edit(e, curve, moved);
        Assert.Equal(first.Count, second.Count);
        Assert.DoesNotContain(e.Edges, x => x.Curve == curve);
        Assert.Equal(first.Count + 1, e.Edges.Count);
        Assert.Contains(e.Vertices, v => v.Position.DistanceTo(new Vec3(40, 5, 0)) < 1e-9);
        Assert.DoesNotContain(e.Vertices, v => v.Position.DistanceTo(new Vec3(30, 0, 0)) < 1e-9);
        Assert.Same(moved, second[0].Curve!.Spline);
    }

    [Fact]
    public void Editing_a_closed_spline_keeps_its_face()
    {
        var e = new Entities();
        var data = new SplineData(SplineKind.CatmullSpline, [new(0, 0, 0), new(20, 0, 0), new(20, 20, 0), new(0, 20, 0)], 7, 0, true);
        var curve = Splines.Draw(e, data)[0].Curve!;
        Assert.Single(e.Faces);
        Splines.Edit(e, curve, data with { ControlPoints = [new(0, 0, 0), new(30, 0, 0), new(30, 20, 0), new(0, 20, 0)] });
        Assert.Single(e.Faces);
        Assert.Equal(e.Edges.Count, e.Faces[0].OuterLoop.Edges.Count);
        Assert.True(e.Vertices.Max(v => v.Position.X) > 29);
    }

    [Fact]
    public void T_bone_corners_notch_along_the_incoming_side()
    {
        // A 20 × 10 rectangle's corner at (20, 0): the notch runs along the bottom side, past the corner outwards.
        var pts = Splines.Compute(SplineKind.TBone, [new(0, 0, 0), new(20, 0, 0), new(20, 10, 0)], 24, 2, false);
        Assert.Equal(new Vec3(0, 0, 0), pts[0]);
        Assert.Equal(new Vec3(20, 10, 0), pts[^1]);
        Assert.Contains(pts, p => p.DistanceTo(new Vec3(16, 0, 0)) < 1e-9);
        Assert.Contains(pts, p => p.DistanceTo(new Vec3(20, 0, 0)) < 1e-9);
        Assert.Contains(pts, p => p.DistanceTo(new Vec3(18, -2, 0)) < 1e-9);
        Assert.All(pts, p => Assert.True(p.Y >= -2 - 1e-9 && p.X <= 20 + 1e-9));
    }

    [Fact]
    public void Closing_with_a_line_adds_a_straight_edge_back_to_the_start()
    {
        var e = new Entities();
        var data = new SplineData(SplineKind.CubicBezier, [new(0, 0, 0), new(10, 10, 0), new(20, 10, 0), new(30, 0, 0)], 7, 0, false, LineClosed: true);
        var edges = Splines.Draw(e, data);
        Assert.Contains(edges, x => x.Start.Position.DistanceTo(Vec3.Zero) < 1e-9 && x.End.Position.DistanceTo(new Vec3(30, 0, 0)) < 1e-9
            || x.End.Position.DistanceTo(Vec3.Zero) < 1e-9 && x.Start.Position.DistanceTo(new Vec3(30, 0, 0)) < 1e-9);
        Assert.Single(e.Faces);
    }

    [Theory]
    [InlineData(AnimationSteps.EqualMaximum)]
    [InlineData(AnimationSteps.Accelerate)]
    [InlineData(AnimationSteps.Decelerate)]
    [InlineData(AnimationSteps.AccelerateDecelerate)]
    public void Animation_steps_cover_the_curve_and_change_as_asked(AnimationSteps mode)
    {
        var steps = Splines.AnimationStepLengths(300, mode, 5, 20);
        Assert.Equal(300, steps.Sum(), 6);
        switch (mode)
        {
            case AnimationSteps.EqualMaximum:
                Assert.All(steps, s => Assert.Equal(20, s, 6));
                break;
            case AnimationSteps.Accelerate:
                Assert.True(steps[0] < steps[^1]);
                Assert.Equal(5, steps[0], 6);
                break;
            case AnimationSteps.Decelerate:
                Assert.True(steps[0] > steps[^1]);
                break;
            default:
                Assert.True(steps[steps.Count / 2] > steps[0] && steps[steps.Count / 2] > steps[^1]);
                break;
        }

        var line = new List<Vec3> { Vec3.Zero, new(100, 0, 0), new(100, 200, 0) };
        var pts = Splines.DivideForAnimation(line, mode, 5, 20);
        Assert.Equal(line[0], pts[0]);
        Assert.Equal(line[^1], pts[^1]);
        Assert.InRange(pts.Count, steps.Count, steps.Count + 1);
    }
}
