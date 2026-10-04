using System.Text;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;

namespace Dogeometric.Core.Tests;

public class HiddenLineTests
{
    private static readonly Vec3 Dir = new Vec3(1, 2, -1.5).Normalized();

    private static (Func<Vec3, (double X, double Y)?> ToScreen, Func<Vec3, Ray> RayTo) Parallel()
    {
        var (u, v) = Polygon.PlaneAxes(Dir);
        return (p => (p.Dot(u) * 10, -p.Dot(v) * 10), p => new Ray(p - Dir * 1000, Dir));
    }

    [Fact]
    public void A_box_shows_nine_edges_and_hides_the_three_at_its_far_corner()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        var (toScreen, rayTo) = Parallel();
        var segments = HiddenLine.Visible(m, toScreen, rayTo, new Picker(), stepPixels: 1);
        double Len(double x1, double y1, double x2, double y2) => Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        var shown = segments.Sum(s => Len(s.X1, s.Y1, s.X2, s.Y2));
        var far = new Vec3(10, 10, 0);
        var expected = m.Entities.Edges.Where(e => e.Start.Position != far && e.End.Position != far)
            .Sum(e => { var (a, b) = (toScreen(e.Start.Position)!.Value, toScreen(e.End.Position)!.Value); return Len(a.X, a.Y, b.X, b.Y); });
        Assert.Equal(expected, shown, 0);
    }

    [Fact]
    public void Svg_and_pdf_hold_every_segment()
    {
        List<HiddenLine.Segment> segs = [new(0, 0, 10, 10, false), new(5, 0, 5, 20, true)];
        var svg = HiddenLine.ToSvg(segs, 100, 50);
        Assert.Equal(2, svg.Split("<line").Length - 1);
        var pdf = Encoding.ASCII.GetString(HiddenLine.ToPdf(segs, 100, 50));
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("0 50 m 10 40 l S", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
    }
}
