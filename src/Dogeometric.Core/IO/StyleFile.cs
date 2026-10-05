using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.IO;

/// <summary>
/// SketchUp's .style files (Styles › Import and Save As): a zip whose document.xml lists the style's settings as
/// numbered items, with watermark pictures under ref/. The numbers were matched against SketchUp 2021's rendering
/// options for each of its own styles; items whose meaning is not known are left out, so SketchUp keeps its defaults.
/// </summary>
public static class StyleFile
{
    private static readonly XNamespace Sty = "http://sketchup.google.com/schemas/sketchup/1.0/style";
    private static readonly XNamespace Types = "http://sketchup.google.com/schemas/1.0/types";
    private static readonly XNamespace Wm = "http://sketchup.google.com/schemas/sketchup/1.0/wmlist";

    // Variant types of the items.
    private const int Bool = 1, Int = 4, Colour = 5, Float = 7, List = 13;

    public static StyleSettings Load(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip, Path.GetFileNameWithoutExtension(path));
    }

    private static StyleSettings Read(ZipArchive zip, string fallbackName)
    {
        var entry = zip.GetEntry("document.xml") ?? throw new InvalidDataException("Not a SketchUp style: no document.xml");
        var doc = XDocument.Load(entry.Open());
        var style = doc.Descendants(Sty + "style").FirstOrDefault() ?? throw new InvalidDataException("Not a SketchUp style: no style element");
        var items = style.Elements(Sty + "item").ToDictionary(i => (int)i.Attribute("id")!, i => i.Element(Types + "variant")!);
        var d = new StyleSettings();
        string? Text(int id) => items.TryGetValue(id, out var v) ? v.Value : null;
        bool B(int id, bool f) => Text(id) is { } t ? t != "0" : f;
        int I(int id, int f) => Text(id) is { } t && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : f;
        double F(int id, double f) => Text(id) is { } t && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : f;
        Rgba C(int id, Rgba f) => Text(id) is { } t && int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? FromAbgr(x) : f;

        var renderMode = I(2001, 2);
        var face = B(2004, false) ? FaceStyle.XRay : renderMode switch
        {
            0 => FaceStyle.Wireframe,
            1 => FaceStyle.HiddenLine,
            5 => FaceStyle.Monochrome,
            _ => B(2007, true) ? FaceStyle.ShadedWithTextures : FaceStyle.Shaded,
        };
        return d with
        {
            Name = Unbracket((string?)style.Attribute("name")) ?? fallbackName,
            Description = Unbracket((string?)style.Attribute("desc")) ?? "",
            Edges = I(1000, 1) != 0,
            EdgeColorMode = I(1002, 1) switch { 0 => EdgeColorMode.ByMaterial, 2 => EdgeColorMode.ByAxis, _ => EdgeColorMode.AllSame },
            Extension = B(1004, d.Extension),
            ExtensionLength = I(1005, d.ExtensionLength),
            Profiles = B(1006, d.Profiles),
            ProfileWidth = I(1007, d.ProfileWidth),
            DepthCue = B(1008, d.DepthCue),
            DepthCueWidth = I(1009, d.DepthCueWidth),
            Endpoints = B(1010, d.Endpoints),
            EndpointLength = I(1011, d.EndpointLength),
            Jitter = B(1012, d.Jitter),
            EdgeColor = C(1014, d.EdgeColor),
            BackEdges = B(1015, d.BackEdges),
            FaceStyle = face,
            FrontColor = C(2002, d.FrontColor),
            BackColor = C(2003, d.BackColor),
            Transparency = B(2005, d.Transparency),
            TransparencyQuality = I(2006, 0) == 1 ? TransparencyQuality.Nicer : TransparencyQuality.Faster,
            XrayOpacity = F(2008, d.XrayOpacity),
            BackgroundColor = C(4000, d.BackgroundColor),
            SkyColor = C(4001, d.SkyColor),
            Sky = B(4002, d.Sky),
            GroundColor = C(4003, d.GroundColor),
            Ground = B(4004, d.Ground),
            GroundTransparency = I(4005, 0) / 100.0,
            GroundFromBelow = B(4006, d.GroundFromBelow),
            ShowWatermarks = B(5000, d.ShowWatermarks),
            Watermarks = items.TryGetValue(5001, out var list) ? new ValueList<Watermark>(ReadWatermarks(list, zip)) : d.Watermarks,
            SelectedColor = C(7000, d.SelectedColor),
            LockedColor = C(7001, d.LockedColor),
            GuideColor = C(7002, d.GuideColor),
            ActiveSectionColor = C(7003, d.ActiveSectionColor),
            InactiveSectionColor = C(7004, d.InactiveSectionColor),
            SectionCutColor = C(7005, d.SectionCutColor),
            ModelAxes = B(7008, d.ModelAxes),
            SectionCutWidth = I(7014, d.SectionCutWidth),
            SectionFillColor = C(7016, d.SectionFillColor),
        };
    }

    /// <summary>SketchUp's own styles name themselves "[Default Style]" (a translation key).</summary>
    private static string? Unbracket(string? s) => s is ['[', .. var inner, ']'] ? inner : s;

    private static IEnumerable<Watermark> ReadWatermarks(XElement variant, ZipArchive zip)
    {
        foreach (var e in variant.Descendants(Wm + "screenimage"))
        {
            if ((string?)e.Element(Wm + "images")?.Element(Wm + "image")?.Attribute("path") is not { } file
                || zip.GetEntry("ref/" + file) is not { } entry)
                continue;
            using var stream = entry.Open();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            double D(string a, double f) => double.TryParse((string?)e.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : f;
            bool On(string a) => (string?)e.Attribute(a) == "1";
            yield return new Watermark
            {
                Name = (string?)e.Attribute("name") ?? "",
                Image = new TextureImage { FileName = file, Data = data.ToArray() },
                Overlay = !On("background"),
                Mask = On("intensForAlpha"),
                Opacity = D("alphaScale", 1),
                Scale = D("scale", 1),
                Layout = On("stretched") ? WatermarkLayout.Stretched : On("tiled") ? WatermarkLayout.Tiled : WatermarkLayout.Positioned,
                LockAspect = On("maintainAR"),
                Position = (WatermarkPosition)Math.Clamp((int)D("position", 8), 0, 8),
            };
        }
    }

    public static void Save(StyleSettings s, string path)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        var items = new SortedDictionary<int, string>();
        void Item(int id, int type, string value) => items[id] = $"<t:variant type=\"{type}\">{value}</t:variant>";
        void B(int id, bool v) => Item(id, Bool, v ? "1" : "0");
        void I(int id, int v) => Item(id, Int, v.ToString(CultureInfo.InvariantCulture));
        void F(int id, double v) => Item(id, Float, v.ToString("R", CultureInfo.InvariantCulture));
        void C(int id, Rgba c) => Item(id, Colour, ToAbgr(c).ToString(CultureInfo.InvariantCulture));

        I(1000, s.Edges ? 1 : 0);
        I(1002, s.EdgeColorMode switch { EdgeColorMode.ByMaterial => 0, EdgeColorMode.ByAxis => 2, _ => 1 });
        B(1004, s.Extension);
        I(1005, s.ExtensionLength);
        B(1006, s.Profiles);
        I(1007, s.ProfileWidth);
        B(1008, s.DepthCue);
        I(1009, s.DepthCueWidth);
        B(1010, s.Endpoints);
        I(1011, s.EndpointLength);
        B(1012, s.Jitter);
        C(1014, s.EdgeColor);
        B(1015, s.BackEdges);
        I(2001, s.FaceStyle switch { FaceStyle.Wireframe => 0, FaceStyle.HiddenLine => 1, FaceStyle.Monochrome => 5, _ => 2 });
        C(2002, s.FrontColor);
        C(2003, s.BackColor);
        B(2004, s.FaceStyle == FaceStyle.XRay);
        B(2005, s.Transparency);
        I(2006, s.TransparencyQuality == TransparencyQuality.Nicer ? 1 : 0);
        B(2007, s.FaceStyle is FaceStyle.ShadedWithTextures or FaceStyle.XRay);
        F(2008, s.XrayOpacity);
        C(4000, s.BackgroundColor);
        C(4001, s.SkyColor);
        B(4002, s.Sky);
        C(4003, s.GroundColor);
        B(4004, s.Ground);
        I(4005, (int)Math.Round(s.GroundTransparency * 100));
        B(4006, s.GroundFromBelow);
        B(5000, s.ShowWatermarks);
        C(7000, s.SelectedColor);
        C(7001, s.LockedColor);
        C(7002, s.GuideColor);
        C(7003, s.ActiveSectionColor);
        C(7004, s.InactiveSectionColor);
        C(7005, s.SectionCutColor);
        B(7008, s.ModelAxes);
        I(7014, s.SectionCutWidth);
        B(7015, true);
        I(7016, ToAbgr(s.SectionFillColor));

        // Watermarks: the background ones, SketchUp's "<MODEL SPACE>" divider, then the overlays; the count includes the divider.
        var marks = new StringBuilder();
        var marked = s.Watermarks.Where(m => !m.Overlay).Append(null).Concat(s.Watermarks.Where(m => m.Overlay)).ToList();
        for (var i = 0; i < marked.Count; i++)
        {
            if (marked[i] is not { } m)
            {
                marks.Append("<n0:screenimage name=\"&lt;MODEL SPACE&gt;\" />");
                continue;
            }
            var picture = $"{Path.GetFileNameWithoutExtension(m.Image.FileName)}_{i}{Path.GetExtension(m.Image.FileName)}";
            using (var es = zip.CreateEntry("ref/" + picture).Open())
                es.Write(m.Image.Data);
            string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            marks.Append($"<n0:screenimage name=\"{X(m.Name)}\" info_found=\"1\" info_filename=\"{X(m.Image.FileName)}\" info_time=\"0\" ")
                .Append($"position=\"{(int)m.Position}\" tiled=\"{(m.Layout == WatermarkLayout.Tiled ? 1 : 0)}\" stretched=\"{(m.Layout == WatermarkLayout.Stretched ? 1 : 0)}\" ")
                .Append($"maintainAR=\"{(m.LockAspect ? 1 : 0)}\" background=\"{(m.Overlay ? 0 : 1)}\" intensForAlpha=\"{(m.Mask ? 1 : 0)}\" ")
                .Append($"alphaScale=\"{N(m.Opacity)}\" scale=\"{N(m.Scale)}\"><n0:images><n0:image id=\"{i}\" path=\"{X(picture)}\" /></n0:images></n0:screenimage>");
        }
        items[5001] = $"<t:variant type=\"{List}\" xmlns:n0=\"{Wm}\"><n0:wmlist count=\"{marked.Count}\"><n0:screenimages>{marks}</n0:screenimages></n0:wmlist></t:variant>";

        const string head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\" ?>";
        var document = new StringBuilder(head)
            .Append("<styleDocument xmlns=\"http://sketchup.google.com/schemas/sketchup/1.0/style\" xmlns:sty=\"http://sketchup.google.com/schemas/sketchup/1.0/style\" ")
            .Append("xmlns:r=\"http://sketchup.google.com/schemas/1.0/references\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ")
            .Append("xsi:schemaLocation=\"http://sketchup.google.com/schemas/sketchup/1.0/style http://sketchup.google.com/schemas/sketchup/1.0/style.xsd\">")
            .Append($"<sty:style xmlns:t=\"http://sketchup.google.com/schemas/1.0/types\" name=\"{X(s.Name)}\" desc=\"{X(s.Description)}\">");
        foreach (var (id, variant) in items)
            document.Append($"<sty:item id=\"{id}\">{variant}</sty:item>");
        document.Append("</sty:style></styleDocument>");
        Write(zip, "document.xml", document.ToString());
        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        Write(zip, "documentProperties.xml", head
            + "<documentProperties xmlns=\"http://sketchup.google.com/schemas/1.0/documentproperties\" xmlns:dp=\"http://sketchup.google.com/schemas/1.0/documentproperties\" "
            + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:dp_style=\"http://sketchup.google.com/schemas/sketchup/1.0/style_document_properties\" "
            + "xsi:schemaLocation=\"http://sketchup.google.com/schemas/sketchup/1.0/style_document_properties http://sketchup.google.com/schemas/1.0/documentproperties.xsd\">"
            + $"<dp_style:fastModeling>0</dp_style:fastModeling><dp:title>{X(s.Name)}</dp:title><dp:description>{X(s.Description)}</dp:description>"
            + "<dp:creator></dp:creator><dp:keywords></dp:keywords><dp:lastModifiedBy></dp:lastModifiedBy><dp:revision>1</dp:revision>"
            + $"<dp:created>{now}</dp:created><dp:modified>{now}</dp:modified><dp:thumbnail>doc_thumbnail.png</dp:thumbnail><dp:generator dp:name=\"Style\" dp:version=\"1\" /></documentProperties>");
        Write(zip, "references.xml", head
            + "<references xmlns=\"http://sketchup.google.com/schemas/1.0/references\" xmlns:r=\"http://sketchup.google.com/schemas/1.0/references\" "
            + "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"http://sketchup.google.com/schemas/1.0/references http://sketchup.google.com/schemas/1.0/references.xsd\" />");
        using (var thumb = zip.CreateEntry("doc_thumbnail.png").Open())
            thumb.Write(Thumbnail(s.BackgroundColor));
    }

    private static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    private static void Write(ZipArchive zip, string name, string text)
    {
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>A one-pixel PNG of the style's background colour, standing in for SketchUp's rendered thumbnail.</summary>
    private static byte[] Thumbnail(Rgba c)
    {
        byte[] raw = [0, c.R, c.G, c.B];
        using var deflated = new MemoryStream();
        using (var z = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            png.Write(BigEndian((uint)data.Length));
            png.Write(body);
            png.Write(BigEndian(Crc32(body)));
        }
        Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        Chunk("IDAT", deflated.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>SketchUp stores colours as signed 32-bit integers with red in the lowest byte.</summary>
    private static Rgba FromAbgr(int v) => new((byte)v, (byte)(v >> 8), (byte)(v >> 16));

    private static int ToAbgr(Rgba c) => unchecked((int)(0xFF000000u | (uint)(c.B << 16) | (uint)(c.G << 8) | c.R));
}
