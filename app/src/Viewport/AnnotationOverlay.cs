using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Viewport;

/// <summary>
/// Draws dimensions and text labels on top of the view as SketchUp does: lines from model points, text aligned to
/// the screen. Remembers where each one landed on screen so the Select tool can pick them.
/// </summary>
public sealed class AnnotationOverlay
{
    private const int FontSize = 13;
    private const float ArrowLength = 9;
    private const float ArrowHalfWidth = 3;
    private static readonly Color Ink = Colors.Black;
    private static readonly Color Selected = new(0, 0, 1);

    private readonly List<(object Item, IReadOnlyList<Entities> Owner, Rect2? Text, (Vector2 A, Vector2 B)[] Lines)> _drawn = [];

    public void Draw(ModelViewport view, Control canvas)
    {
        _drawn.Clear();
        if (view.Document is not { } doc)
            return;
        var font = canvas.GetThemeDefaultFont();

        // Section cut: thick black lines where the active section slices the model.
        if (view.ShowSectionCuts)
            foreach (var (a, b) in view.SectionCut)
                if (view.ToScreen(a) is { } sa && view.ToScreen(b) is { } sb)
                    canvas.DrawLine(sa, sb, Ink, 3, true);

        Walk(doc.Model.Entities, Transform.Identity, [doc.Model.Entities]);

        void Walk(Entities e, Transform xf, IReadOnlyList<Entities> owners)
        {
            foreach (var d in e.Dimensions)
                if (!d.Hidden && d.Tag is not { Visible: false })
                    DrawDimension(view, canvas, font, doc, d, xf, owners);
            foreach (var t in e.Texts)
                if (!t.Hidden && t.Tag is not { Visible: false })
                    DrawText(view, canvas, font, doc, t, xf, owners);
            if (view.ShowSectionPlanes)
                foreach (var s in e.SectionPlanes)
                    if (!s.Hidden && s.Tag is not { Visible: false })
                        DrawSectionPlane(view, canvas, doc, s, s == e.ActiveSection, xf, owners);
            foreach (var inst in e.Instances)
                if (!inst.Hidden && inst.Tag is not { Visible: false })
                    Walk(inst.Definition.Entities, inst.Transform.Then(xf), [.. owners, inst.Definition.Entities]);
        }
    }

    /// <summary>The dimension or text drawn under <paramref name="screen"/>, with the collection that holds it.</summary>
    public (object Item, Entities Owner)? Pick(Vector2 screen, float radius = 5)
    {
        for (var i = _drawn.Count - 1; i >= 0; i--)
        {
            var (item, owners, text, lines) = _drawn[i];
            if (text is { } r && r.Grow(2).HasPoint(screen) || lines.Any(l => DistanceToSegment(screen, l.A, l.B) <= radius))
                return (item, owners[^1]);
        }
        return null;
    }

    private void DrawDimension(ModelViewport view, Control canvas, Font font, Document doc, LinearDimension d, Transform xf, IReadOnlyList<Entities> owners)
    {
        if (view.ToScreen(xf.ApplyPoint(d.Start)) is not { } p1 || view.ToScreen(xf.ApplyPoint(d.End)) is not { } p2 ||
            view.ToScreen(xf.ApplyPoint(d.Start + d.Offset)) is not { } q1 || view.ToScreen(xf.ApplyPoint(d.End + d.Offset)) is not { } q2)
            return;
        var color = doc.Selection.Contains(d) ? Selected : Ink;
        var lines = new List<(Vector2, Vector2)>();

        // Extension lines run from the measured points past the dimension line by a few pixels.
        foreach (var (p, q) in new[] { (p1, q1), (p2, q2) })
        {
            var dir = (q - p).Length() > 1e-3 ? (q - p).Normalized() : Vector2.Zero;
            canvas.DrawLine(p, q + dir * 4, color, 1, true);
            lines.Add((p, q));
        }
        canvas.DrawLine(q1, q2, color, 1, true);
        lines.Add((q1, q2));
        if ((q2 - q1).Length() > 2 * ArrowLength)
        {
            Arrow(canvas, q1, (q1 - q2).Normalized(), color);
            Arrow(canvas, q2, (q2 - q1).Normalized(), color);
        }

        var model = doc.Model;
        var measured = Length.Format(d.Length, model.Units, model.UnitPrecision);
        var text = d.Text.Length == 0 ? measured : d.Text.Replace("<>", measured);
        var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize);
        var mid = (q1 + q2) / 2;
        // Screen-aligned text centred over the middle of the dimension line.
        var box = new Rect2(mid - new Vector2(size.X / 2, size.Y + 2), size);
        canvas.DrawString(font, box.Position + new Vector2(0, size.Y - 3), text, HorizontalAlignment.Left, -1, FontSize, color);
        _drawn.Add((d, owners, box, [.. lines]));
    }

    private void DrawText(ModelViewport view, Control canvas, Font font, Document doc, TextLabel t, Transform xf, IReadOnlyList<Entities> owners)
    {
        var color = doc.Selection.Contains(t) ? Selected : Ink;
        var size = font.GetStringSize(t.Text, HorizontalAlignment.Left, -1, FontSize);
        if (t.ScreenPosition is { } sp)
        {
            var at = new Vector2((float)sp.X * view.Size.X, (float)sp.Y * view.Size.Y);
            var screenBox = new Rect2(at, size);
            canvas.DrawString(font, at + new Vector2(0, size.Y - 3), t.Text, HorizontalAlignment.Left, -1, FontSize, color);
            _drawn.Add((t, owners, screenBox, []));
            return;
        }
        if (view.ToScreen(xf.ApplyPoint(t.Point)) is not { } anchor || view.ToScreen(xf.ApplyPoint(t.Point + t.Offset)) is not { } end)
            return;
        // Leader with a dot on the model; the text sits beyond the leader's end, on the side it points to.
        canvas.DrawLine(anchor, end, color, 1, true);
        canvas.DrawCircle(anchor, 2.5f, color);
        var left = end.X < anchor.X;
        var box = new Rect2(new Vector2(left ? end.X - size.X - 3 : end.X + 3, end.Y - size.Y / 2), size);
        canvas.DrawString(font, box.Position + new Vector2(0, size.Y - 3), t.Text, HorizontalAlignment.Left, -1, FontSize, color);
        _drawn.Add((t, owners, box, [(anchor, end)]));
    }

    /// <summary>Half the side of the square drawn for section planes: a bit larger than the model.</summary>
    private static double SectionHalfSize(Document doc) => Math.Max(doc.Model.Entities.Bounds().Diagonal * 0.6, 100);

    /// <summary>A section plane: an orange square with an arrow at each corner pointing the way the cut is viewed.</summary>
    public static IEnumerable<(Vec3 A, Vec3 B)> SectionPlaneLines(SectionPlane s, Transform xf, double half)
    {
        var n = xf.ApplyNormal(s.Normal).Normalized();
        var c = xf.ApplyPoint(s.Point);
        var (u, v) = Polygon.PlaneAxes(n);
        Vec3[] corners = [c + (u + v) * half, c + (v - u) * half, c - (u + v) * half, c + (u - v) * half];
        for (var i = 0; i < 4; i++)
            yield return (corners[i], corners[(i + 1) % 4]);
        var arrow = half * 0.18;
        foreach (var corner in corners)
        {
            var inward = (c - corner).Normalized() * arrow * 0.35;
            var tip = corner + n * arrow;
            yield return (corner, tip);
            yield return (tip, tip - n * arrow * 0.4 + inward);
            yield return (tip, tip - n * arrow * 0.4 - inward);
        }
    }

    private void DrawSectionPlane(ModelViewport view, Control canvas, Document doc, SectionPlane s, bool active, Transform xf, IReadOnlyList<Entities> owners)
    {
        var color = doc.Selection.Contains(s) ? Selected : active ? new Color("#f28c28") : new Color("#a0a0a0");
        var lines = new List<(Vector2, Vector2)>();
        foreach (var (a, b) in SectionPlaneLines(s, xf, SectionHalfSize(doc)))
        {
            if (view.ToScreen(a) is not { } sa || view.ToScreen(b) is not { } sb)
                continue;
            canvas.DrawLine(sa, sb, color, active ? 2 : 1, true);
            lines.Add((sa, sb));
        }
        _drawn.Add((s, owners, null, [.. lines]));
    }

    private static void Arrow(Control canvas, Vector2 tip, Vector2 outward, Color color)
    {
        // Closed arrowhead pointing outwards at the extension line.
        var back = tip - outward * ArrowLength;
        var side = new Vector2(-outward.Y, outward.X) * ArrowHalfWidth;
        canvas.DrawColoredPolygon([tip, back + side, back - side], color);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() < 1e-6f ? 0 : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return p.DistanceTo(a + ab * t);
    }
}
