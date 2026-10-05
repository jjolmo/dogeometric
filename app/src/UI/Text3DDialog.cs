using Dogeometric.Core.Geometry;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.UI;

/// <summary>
/// SketchUp's Place 3D Text dialog: text, font, style (regular, bold, italic), alignment, height, filled and extruded,
/// opening with the last values used. Produces the letter outlines, flattened, in millimetres on the red-green plane
/// (first line's baseline at y = 0).
/// </summary>
public partial class Text3DDialog : ConfirmationDialog
{
    public sealed record Result(string Text, List<List<Vec3>> Contours, bool Filled, double Extrude);

    private TextEdit _text = null!;
    private OptionButton _font = null!;
    private OptionButton _style = null!;
    private static (string Text, string Family, int Style, int Align, string Height, bool Filled, bool Extruded, string Depth)? _last;
    private OptionButton _align = null!;
    private LineEdit _height = null!;
    private CheckBox _filled = null!;
    private CheckBox _extruded = null!;
    private LineEdit _depth = null!;

    public static void Show(Node parent, Action<Result> place)
    {
        var d = new Text3DDialog { Title = "Place 3D Text", OkButtonText = "Place" };
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        d._text = new TextEdit { Text = "Enter text", CustomMinimumSize = new Vector2(0, 80) };
        box.AddChild(d._text);

        var grid = new GridContainer { Columns = 2 };
        grid.AddChild(new Label { Text = "Font" });
        var fontRow = new HBoxContainer();
        d._font = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var name in OS.GetSystemFonts().OrderBy(n => n))
            d._font.AddItem(name);
        if (d._font.ItemCount == 0)
            d._font.AddItem("Sans");
        for (var i = 0; i < d._font.ItemCount; i++)
            if (d._font.GetItemText(i) is "Tahoma" or "DejaVu Sans" or "Noto Sans")
                d._font.Select(i);
        fontRow.AddChild(d._font);
        d._style = new OptionButton();
        foreach (var style in new[] { "Regular", "Bold", "Italic", "Bold Italic" })
            d._style.AddItem(style);
        fontRow.AddChild(d._style);
        grid.AddChild(fontRow);

        grid.AddChild(new Label { Text = "Align" });
        d._align = new OptionButton();
        d._align.AddItem("Left");
        d._align.AddItem("Center");
        d._align.AddItem("Right");
        grid.AddChild(d._align);

        grid.AddChild(new Label { Text = "Height" });
        d._height = new LineEdit { Text = "25.4mm" };
        grid.AddChild(d._height);

        grid.AddChild(new Label { Text = "Form" });
        d._filled = new CheckBox { Text = "Filled", ButtonPressed = true };
        grid.AddChild(d._filled);

        d._extruded = new CheckBox { Text = "Extruded", ButtonPressed = true };
        grid.AddChild(d._extruded);
        d._depth = new LineEdit { Text = "5mm" };
        d._extruded.Toggled += on => d._depth.Editable = on;
        grid.AddChild(d._depth);
        box.AddChild(grid);
        d.AddChild(box);

        // The dialog opens with what was used last, as SketchUp's does.
        if (_last is var (lastText, lastFamily, lastStyle, lastAlign, lastHeight, lastFilled, lastExtruded, lastDepth))
        {
            d._text.Text = lastText;
            for (var i = 0; i < d._font.ItemCount; i++)
                if (d._font.GetItemText(i) == lastFamily)
                    d._font.Select(i);
            d._style.Select(lastStyle);
            d._align.Select(lastAlign);
            d._height.Text = lastHeight;
            d._filled.ButtonPressed = lastFilled;
            d._extruded.ButtonPressed = lastExtruded;
            d._depth.Text = lastDepth;
            d._depth.Editable = lastExtruded;
        }

        d.Confirmed += () =>
        {
            var text = d._text.Text;
            if (!UI.Measure.Read(d._height.Text, out var height) || height <= 0)
                height = 25.4;
            var depth = d._extruded.ButtonPressed && UI.Measure.Read(d._depth.Text, out var e) ? e : 0;
            var family = d._font.GetItemText(d._font.Selected);
            _last = (text, family, d._style.Selected, d._align.Selected, d._height.Text, d._filled.ButtonPressed, d._extruded.ButtonPressed, d._depth.Text);
            var contours = Outlines(text, family, d._style.Selected is 1 or 3, d._align.Selected, height, italic: d._style.Selected >= 2);
            if (contours.Count > 0)
                place(new Result(text, contours, d._filled.ButtonPressed, depth));
            d.QueueFree();
        };
        d.Canceled += d.QueueFree;
        parent.AddChild(d);
        d.PopupCentered();
        d._text.GrabFocus();
        d._text.SelectAll();
    }

    /// <summary>Letter outlines for <paramref name="text"/>, scaled so capitals are <paramref name="height"/> mm.</summary>
    public static List<List<Vec3>> Outlines(string text, string family, bool bold, int align, double height, bool italic = false)
    {
        const int size = 128; // shaping size in pixels; curves are flattened at this resolution
        var font = new SystemFont { FontNames = [family], FontWeight = bold ? 700 : 400, FontItalic = italic };
        var ts = TextServerManager.GetPrimaryInterface();

        // Capital height from the "H" glyph, so Height means what SketchUp's does.
        var capHeight = (double)size * 0.7;
        var probe = Shape(ts, font, "H", size);
        if (GlyphPolylines(ts, probe, 0).SelectMany(c => c).ToList() is { Count: > 0 } hPoints)
            capHeight = hPoints.Max(p => p.Y) - hPoints.Min(p => p.Y);
        var scale = height / capHeight;

        var result = new List<List<Vec3>>();
        var lines = text.Replace("\r", "").Split('\n');
        var widths = new List<double>();
        var perLine = new List<List<List<Vec3>>>();
        foreach (var line in lines)
        {
            var shaped = Shape(ts, font, line, size);
            perLine.Add(GlyphPolylines(ts, shaped, 0));
            widths.Add(ts.ShapedTextGetWidth(shaped));
        }
        var maxWidth = widths.Count > 0 ? widths.Max() : 0;
        for (var li = 0; li < perLine.Count; li++)
        {
            var dx = align switch { 1 => (maxWidth - widths[li]) / 2, 2 => maxWidth - widths[li], _ => 0 };
            var dy = -li * capHeight * 1.6;
            foreach (var c in perLine[li])
                result.Add(c.Select(p => new Vec3((p.X + dx) * scale, (p.Y + dy) * scale, 0)).ToList());
        }
        return result;
    }

    private static Rid Shape(TextServer ts, Font font, string text, int size)
    {
        var shaped = ts.CreateShapedText();
        ts.ShapedTextAddString(shaped, text, font.GetRids(), size);
        ts.ShapedTextShape(shaped);
        return shaped;
    }

    /// <summary>Each glyph's contours as closed polylines in pixels, y up, laid out along the line.</summary>
    private static List<List<Vec3>> GlyphPolylines(TextServer ts, Rid shaped, double y)
    {
        var result = new List<List<Vec3>>();
        double penX = 0;
        foreach (var g in ts.ShapedTextGetGlyphs(shaped))
        {
            var fontRid = g["font_rid"].AsRid();
            var index = g["index"].AsInt64();
            var fontSize = g["font_size"].AsInt64();
            var advance = g["advance"].AsDouble();
            var offset = g["offset"].AsVector2();
            if (fontRid.IsValid)
            {
                var data = ts.FontGetGlyphContours(fontRid, fontSize, index);
                if (data.Count > 0)
                {
                    var points = data["points"].AsVector3Array();
                    var ends = data["contours"].AsInt32Array();
                    var start = 0;
                    foreach (var end in ends)
                    {
                        var poly = Flatten(points, start, end);
                        start = end + 1;
                        if (poly.Count >= 3)
                            result.Add(poly.Select(p => new Vec3(penX + offset.X + p.X, y - (offset.Y + p.Y), 0)).ToList());
                    }
                }
            }
            penX += advance;
        }
        return result;
    }

    /// <summary>One TrueType/CFF contour (on-curve, conic and cubic points) as a polyline.</summary>
    private static List<Vector2> Flatten(Vector3[] pts, int start, int end)
    {
        const int steps = 6;
        var n = end - start + 1;
        Vector2 P(int i) => new(pts[start + ((i % n) + n) % n].X, pts[start + ((i % n) + n) % n].Y);
        int Tag(int i) => (int)pts[start + ((i % n) + n) % n].Z;
        // Start at an on-curve point (or between two conic controls).
        var first = Enumerable.Range(0, n).FirstOrDefault(i => Tag(i) == 1, -1);
        var output = new List<Vector2>();
        Vector2 current;
        int i0;
        if (first < 0)
        {
            current = (P(0) + P(1)) / 2;
            i0 = 1;
        }
        else
        {
            current = P(first);
            i0 = first + 1;
        }
        output.Add(current);
        var k = 0;
        while (k < n)
        {
            var i = i0 + k;
            var tag = Tag(i);
            if (tag == 1)
            {
                current = P(i);
                output.Add(current);
                k++;
            }
            else if (tag == 0)
            {
                // Conic: control at i, ends at the next on-curve point or the midpoint to the next control.
                var control = P(i);
                var nextIsOn = Tag(i + 1) == 1;
                var to = nextIsOn ? P(i + 1) : (control + P(i + 1)) / 2;
                for (var s = 1; s <= steps; s++)
                {
                    var t = s / (float)steps;
                    output.Add((1 - t) * (1 - t) * current + 2 * (1 - t) * t * control + t * t * to);
                }
                current = to;
                k += nextIsOn ? 2 : 1;
            }
            else
            {
                // Cubic: two controls then an on-curve point.
                var c1 = P(i);
                var c2 = P(i + 1);
                var to = P(i + 2);
                for (var s = 1; s <= steps; s++)
                {
                    var t = s / (float)steps;
                    var u = 1 - t;
                    output.Add(u * u * u * current + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * to);
                }
                current = to;
                k += 3;
            }
        }
        // Closed: drop the repeated start and points closer than a hair.
        var clean = new List<Vector2>();
        foreach (var p in output)
            if (clean.Count == 0 || clean[^1].DistanceTo(p) > 0.01f)
                clean.Add(p);
        if (clean.Count > 1 && clean[0].DistanceTo(clean[^1]) <= 0.01f)
            clean.RemoveAt(clean.Count - 1);
        return clean;
    }
}
