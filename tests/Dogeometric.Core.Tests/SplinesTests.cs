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
}
