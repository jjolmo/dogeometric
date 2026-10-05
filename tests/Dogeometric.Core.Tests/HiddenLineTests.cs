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

    [Fact]
    public void A_dxf_drawing_reads_back_at_full_scale()
    {
        List<HiddenLine.Segment> square = [new(10, 10, 50, 10, false), new(50, 10, 50, 50, false), new(50, 50, 10, 50, false), new(10, 50, 10, 10, false)];
        var dxf = HiddenLine.ToDxf(square, 100, 0.5);
        var e = DxfImport.Read(dxf).Definitions[0].Entities;
        Assert.Equal(new Vec3(20, 20, 0), e.Bounds().Size);
        Assert.Single(e.Faces);
    }

    [Fact]
    public void An_active_section_leaves_out_what_it_cuts_away_and_adds_its_cut_lines()
    {
        var m = new Model();
        TestModels.Box(m.Entities, Vec3.Zero, new Vec3(10, 10, 10));
        // Arrows towards -x at x = 5: the half beyond (x > 5) is removed.
        var plane = new SectionPlane(new Vec3(5, 0, 0), new Vec3(-1, 0, 0));
        m.Entities.SectionPlanes.Add(plane);
        m.Entities.ActiveSection = plane;
        var (toScreen, rayTo) = Parallel();
        var segments = HiddenLine.Visible(m, toScreen, rayTo, new Picker(), stepPixels: 1);
        Assert.Contains(segments, s => s.Section);
        Assert.Contains(segments, s => !s.Section);
        // The removed half's corners (x = 10) are no segment's end.
        var removed = m.Entities.Vertices.Where(v => v.Position.X == 10).Select(v => toScreen(v.Position)!.Value).ToList();
        Assert.All(segments, s => Assert.DoesNotContain(removed, c => Math.Abs(c.X - s.X1) + Math.Abs(c.Y - s.Y1) < 0.5 || Math.Abs(c.X - s.X2) + Math.Abs(c.Y - s.Y2) < 0.5));
    }

    [Fact]
    public void Drawing_options_size_the_paper_and_set_the_line_widths()
    {
        List<HiddenLine.Segment> segs = [new(0, 0, 100, 0, false), new(0, 10, 100, 10, true), new(0, 20, 100, 20, false, Section: true)];
        var lines = new HiddenLine.Lines(MmPerPixel: 0.5, EdgeMm: 0.25, ProfileMm: 0.5, ExtensionMm: 1);
        var svg = HiddenLine.ToSvg(segs, 200, 100, lines);
        Assert.Contains("width=\"100mm\" height=\"50mm\"", svg);
        // Widths in view pixels: 0.25 mm / 0.5 = 0.5, 0.5 mm / 0.5 = 1, section matches the screen (3).
        Assert.Contains("stroke-width=\"0.5\"", svg);
        Assert.Contains("stroke-width=\"1\"", svg);
        Assert.Contains("stroke-width=\"3\"", svg);
        // The edge runs 1 mm (2 px) past each end; profiles don't.
        Assert.Contains("x1=\"-2\" y1=\"0\" x2=\"102\"", svg);
        Assert.Contains("x1=\"0\" y1=\"10\" x2=\"100\"", svg);
        var pdf = Encoding.ASCII.GetString(HiddenLine.ToPdf(segs, 200, 100, lines));
        Assert.Contains("/MediaBox [0 0 283.4646 141.7323]", pdf);
        var dxf = HiddenLine.ToDxf(segs, 100, 1, lines);
        Assert.Contains("8\nSections\n370\n", dxf);
    }
}
