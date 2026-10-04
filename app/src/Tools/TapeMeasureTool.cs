using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Tape Measure: measures between two points. Started on an edge it makes an infinite guide parallel
/// to that edge through the end point; started on a point it makes a guide point and a guide line to it. Ctrl
/// toggles guide creation; a typed length sets the guide's distance.
/// </summary>
public sealed class TapeMeasureTool : DrawingTool
{
    private Vec3? _start;
    private (Vec3 A, Vec3 B)? _startEdge;
    private bool _guides = true;

    public override int CommandId => CommandIds.TapeMeasure;
    public override string CursorImage => _guides ? "measureadd" : "measure";
    protected override Vec3? From => _startEdge == null ? _start : null;
    public override string VcbLabel => "Length";

    public override string StatusText => _start == null
        ? "Click an item to measure from" + (_guides ? ".  Ctrl = toggle create guide (on)." : ".  Ctrl = toggle create guide (off).")
        : "Click an item to measure to or enter distance.";

    public override string VcbValue => Measured() is { } d ? Length.Format(d, LengthUnit.Millimeters, 2) : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    /// <summary>Point-to-point distance, or the perpendicular offset from the start edge.</summary>
    private double? Measured()
    {
        if (_start is not { } s || Current is not { } c)
            return null;
        if (_startEdge is { } edge)
            return Perpendicular(edge, c.Point).Length;
        return s.DistanceTo(c.Point);
    }

    private static Vec3 Perpendicular((Vec3 A, Vec3 B) edge, Vec3 p)
    {
        var dir = (edge.B - edge.A).Normalized();
        var rel = p - edge.A;
        return rel - dir * rel.Dot(dir);
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_start == null)
        {
            _start = inf.Point;
            _startEdge = inf is { Kind: InferenceKind.OnEdge, Edge: { } e }
                ? (inf.EntityToWorld.ApplyPoint(e.Start.Position), inf.EntityToWorld.ApplyPoint(e.End.Position))
                : null;
            RefreshStatus();
            return;
        }
        Finish(doc, inf.Point);
    }

    private void Finish(Document doc, Vec3 end)
    {
        if (_guides && _start is { } s)
        {
            var toLocal = doc.Context.ToWorld.Inverse();
            if (_startEdge is { } edge)
            {
                var offset = Perpendicular(edge, end);
                var guide = new GuideLine(toLocal.ApplyPoint(edge.A + offset), toLocal.ApplyVector(edge.B - edge.A));
                doc.Operation("Guide", e => e.GuideLines.Add(guide));
            }
            else if (s.DistanceTo(end) > Tolerance.Length)
            {
                var a = toLocal.ApplyPoint(s);
                var b = toLocal.ApplyPoint(end);
                doc.Operation("Guide", e =>
                {
                    e.GuideLines.Add(new GuideLine(a, b - a) { Start = a, End = b });
                    e.GuidePoints.Add(new GuidePoint(b));
                });
            }
        }
        _start = null;
        _startEdge = null;
        ResetLocks();
        RefreshStatus();
        UpdateInference();
    }

    public override bool ApplyVcb(string text)
    {
        if (_start is not { } s || Current is not { } c || View.Document is not { } doc || !Length.TryParse(text, LengthUnit.Millimeters, out var mm))
            return false;
        Vec3 end;
        if (_startEdge is { } edge)
        {
            var perp = Perpendicular(edge, c.Point);
            if (perp.IsZero(1e-9))
                return false;
            end = edge.A + perp.Normalized() * mm;
        }
        else
        {
            var dir = (c.Point - s).Normalized();
            if (dir.IsZero(1e-9))
                return false;
            end = s + dir * mm;
        }
        Finish(doc, end);
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Ctrl when !key.Echo:
                _guides = !_guides;
                RefreshStatus();
                return true;
            case Key.Escape when _start != null:
                _start = null;
                _startEdge = null;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_start is { } s && Current is { } c)
        {
            if (_startEdge is { } edge)
            {
                // Preview the parallel guide through the cursor.
                var offset = Perpendicular(edge, c.Point);
                var dir = (edge.B - edge.A).Normalized();
                var through = edge.A + offset;
                DrawWorldLine(overlay, through - dir * 100_000, through + dir * 100_000, new Color(0.25f, 0.25f, 0.25f), 1, dashed: true);
                DrawWorldLine(overlay, s, s + offset, Colors.Black, 1);
            }
            else
            {
                DrawWorldLine(overlay, s, c.Point, Colors.Black, 1, dashed: true);
            }
            if (Measured() is { } d && View.ToScreen(c.Point) is { } p)
            {
                var font = overlay.GetThemeDefaultFont();
                overlay.DrawString(font, p + new Vector2(14, -10), Length.Format(d, LengthUnit.Millimeters, 2), HorizontalAlignment.Left, -1, 13, Colors.Black);
            }
        }
        DrawInference(overlay);
    }
}
