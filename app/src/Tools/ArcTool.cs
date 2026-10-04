using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's 2-Point Arc: click the start, click the end (or type the chord length), move sideways to set the
/// bulge (or type it) and click. "12s" in the Measurements box sets the number of segments.
/// </summary>
public sealed class ArcTool : DrawingTool
{
    private static int _segments = Shapes.DefaultArcSegments;

    private Vec3? _start;
    private Vec3? _end;

    public override int CommandId => CommandIds.Arc2Point;
    protected override Vec3? From => _end == null ? _start : null;
    public override string VcbLabel => _end == null ? "Length" : "Bulge";
    public override string StatusText => _start == null ? "Click to set first endpoint." : _end == null ? "Click to set second endpoint or enter length." : "Click to finish.";

    public override string VcbValue => (_start, _end, Current) switch
    {
        ({ } s, null, { } c) => Length.Format(s.DistanceTo(c.Point), LengthUnit.Millimeters, 1),
        ({ } s, { } e, { } c) => Length.Format(Math.Abs(Bulge(s, e, c.Point)), LengthUnit.Millimeters, 1),
        _ => "",
    };

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_start == null)
            _start = inf.Point;
        else if (_end == null)
            _end = inf.Point;
        else
            Create(Bulge(_start.Value, _end.Value, inf.Point), inf.Point);
        RefreshStatus();
    }

    /// <summary>Signed distance of <paramref name="p"/> from the chord, measured in the drawing plane.</summary>
    private double Bulge(Vec3 s, Vec3 e, Vec3 p)
    {
        var chord = (e - s).Normalized();
        var toP = p - (s + e) * 0.5;
        var perp = toP - chord * toP.Dot(chord);
        return perp.Length;
    }

    private Vec3 BulgeDirection(Vec3 s, Vec3 e, Vec3 p)
    {
        var chord = (e - s).Normalized();
        var toP = p - (s + e) * 0.5;
        var perp = toP - chord * toP.Dot(chord);
        if (!perp.IsZero(1e-9))
            return perp.Normalized();
        // Fall back to the axis plane facing the viewer.
        return InferenceEngine.MostFacing(View.Camera.Direction).Cross(chord).Normalized();
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.EndsWith('s') && int.TryParse(t[..^1], out var n))
        {
            if (n < 1 || n > 999)
                return false;
            _segments = n;
            return true;
        }
        if (!Length.TryParse(t, LengthUnit.Millimeters, out var mm) || Current is not { } c)
            return false;
        if (_start is { } s && _end == null)
        {
            var dir = (c.Point - s).Normalized();
            if (dir.IsZero(1e-9))
                return false;
            _end = s + dir * mm;
            RefreshStatus();
            return true;
        }
        if (_start is { } s2 && _end is { } e2)
        {
            Create(mm, c.Point);
            return true;
        }
        return false;
    }

    private void Create(double bulge, Vec3 towards)
    {
        if (_start is not { } s || _end is not { } e || View.Document is not { } doc)
            return;
        var pts = Shapes.TwoPointArc(s, e, BulgeDirection(s, e, towards), bulge, _segments);
        var toLocal = doc.Context.ToWorld.Inverse();
        var local = pts.Select(toLocal.ApplyPoint).ToList();
        var curve = new Curve { Segments = _segments };
        doc.Operation("Arc", ent => StickyGeometry.DrawEdges(ent, local, closed: false, curve: curve));
        _start = null;
        _end = null;
        ResetLocks();
        RefreshStatus();
        UpdateInference();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _start != null)
        {
            _start = null;
            _end = null;
            ResetLocks();
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
            if (_end is { } e)
            {
                var pts = Shapes.TwoPointArc(s, e, BulgeDirection(s, e, c.Point), Bulge(s, e, c.Point), _segments);
                for (var i = 0; i + 1 < pts.Count; i++)
                    DrawWorldLine(overlay, pts[i], pts[i + 1], Colors.Black, 1.5f);
                DrawWorldLine(overlay, s, e, Colors.Black, 1, dashed: true);
            }
            else
            {
                DrawWorldLine(overlay, s, c.Point, AxisColor(c.Kind == InferenceKind.OnAxis ? c.AxisDirection : null), 1.5f);
            }
        }
        DrawInference(overlay);
    }
}
