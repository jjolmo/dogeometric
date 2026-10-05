using System.IO.Compression;
using System.Text;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class StyleFileTests
{
    [Fact]
    public void A_style_survives_saving_as_a_SketchUp_style_file()
    {
        var mark = new Watermark
        {
            Name = "Paper", Image = new TextureImage { FileName = "paper.png", Data = [9, 8, 7] }, Opacity = 0.25,
            Layout = WatermarkLayout.Tiled, Scale = 0.5, Position = WatermarkPosition.Center, LockAspect = false,
        };
        var style = new StyleSettings
        {
            Name = "Plans", Description = "For plans", Profiles = true, ProfileWidth = 4, Extension = true, ExtensionLength = 7,
            Endpoints = true, EndpointLength = 11, Jitter = true, BackEdges = true, EdgeColorMode = EdgeColorMode.ByAxis,
            EdgeColor = new Rgba(10, 20, 30), FaceStyle = FaceStyle.Monochrome, FrontColor = new Rgba(250, 240, 230),
            Transparency = false, TransparencyQuality = TransparencyQuality.Nicer, XrayOpacity = 0.4, Sky = false,
            GroundTransparency = 0.3, SelectedColor = new Rgba(1, 2, 3), SectionFillColor = new Rgba(40, 50, 60), SectionCutWidth = 5,
            ModelAxes = false, Watermarks = new([mark]),
        };
        var path = Path.Combine(Path.GetTempPath(), $"plans-{Guid.NewGuid()}.style");
        StyleFile.Save(style, path);
        var back = StyleFile.Load(path);
        File.Delete(path);
        var read = Assert.Single(back.Watermarks);
        Assert.Equal([9, 8, 7], read.Image.Data);
        Assert.Equal(mark with { Image = read.Image }, read);
        Assert.Equal(style with { Watermarks = back.Watermarks }, back);
    }

    [Fact]
    public void SketchUp_encodings_are_read_as_SketchUp_shows_them()
    {
        // Values as SketchUp 2021 writes them, with what its rendering options report for them.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="no" ?><styleDocument xmlns="http://sketchup.google.com/schemas/sketchup/1.0/style" xmlns:sty="http://sketchup.google.com/schemas/sketchup/1.0/style"><sty:style xmlns:t="http://sketchup.google.com/schemas/1.0/types" name="[Default Style]" desc="[Default colors.]"><sty:item id="1002"><t:variant type="4">1</t:variant></sty:item><sty:item id="1007"><t:variant type="4">2</t:variant></sty:item><sty:item id="1014"><t:variant type="5">-16777216</t:variant></sty:item><sty:item id="2001"><t:variant type="4">2</t:variant></sty:item><sty:item id="2003"><t:variant type="5">-4476252</t:variant></sty:item><sty:item id="2007"><t:variant type="1">0</t:variant></sty:item><sty:item id="4001"><t:variant type="5">-3092035</t:variant></sty:item><sty:item id="4005"><t:variant type="4">50</t:variant></sty:item><sty:item id="7016"><t:variant type="4">-12632257</t:variant></sty:item></sty:style></styleDocument>
            """;
        var path = Path.Combine(Path.GetTempPath(), $"su-{Guid.NewGuid()}.style");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("document.xml").Open(), new UTF8Encoding(false)))
            w.Write(xml);
        var s = StyleFile.Load(path);
        File.Delete(path);
        Assert.Equal("Default Style", s.Name);
        Assert.Equal(EdgeColorMode.AllSame, s.EdgeColorMode);
        Assert.Equal(2, s.ProfileWidth);
        Assert.Equal(new Rgba(0, 0, 0), s.EdgeColor);
        Assert.Equal(FaceStyle.Shaded, s.FaceStyle);
        Assert.Equal(new Rgba(164, 178, 187), s.BackColor);
        Assert.Equal(new Rgba(189, 209, 208), s.SkyColor);
        Assert.Equal(0.5, s.GroundTransparency);
        Assert.Equal(new Rgba(63, 63, 63), s.SectionFillColor);
    }
}
