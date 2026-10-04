using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Rotated Rectangle: click a corner, click the second corner (the first side, any direction), then
/// move off that side to set the width and click. The rectangle stands on the face under the first click, else
/// on the plane facing the viewer through the first side. Lengths can be typed for the side and the width.
/// </summary>
public sealed class RotatedRectangleTool : DrawingTool
{
    private Vec3? _a;
    private Vec3? _b;
    private Vec3 _faceNormal;
    private bool _onFace;

    public override int CommandId => CommandIds.RotatedRectangle;
    public override string CursorImage => "rectangle";
    protected override Vec3? From => _b ?? _a;
    public override string VcbLabel => _b == null ? "Length" : "Width";
    public override string StatusText => (_a, _b) switch
    {
        (null, _) => "Select first corner.",
        (_, null) => "Select second corner or enter length.",
        _ => "Select third corner or enter width.",
    };

    public override string VcbValue => (_a, _b, Current) switch
    {
        ({ } a, null, { } c) => Length.Format(a.DistanceTo(c.Point), LengthUnit.Millimeters, 1),
        ({ }, { }, { }) => Length.Format(Math.Abs(Width()), LengthUnit.Millimeters, 1),
        _ => "",
    };

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_a == null)
        {
            _a = inf.Point;
            _onFace = inf.Face != null;
            if (inf.Face is { } f)
                _faceNormal = inf.EntityToWorld.ApplyNormal(f.Normal).Normalized();
        }
        else if (_b == null)
        {
            if (inf.Point.DistanceTo(_a.Value) <= Tolerance.Length)
                return;
            _b = inf.Point;
        }
        else
        {
            Create(Width());
        }
        RefreshStatus();
    }

    /// <summary>Unit direction across the first side, in the rectangle's plane.</summary>
    private Vec3 Across()
    {
        var side = (_b!.Value - _a!.Value).Normalized();
        var n = _onFace ? _faceNormal : MostFacingPlane();
        var across = n.Cross(side);
        if (across.IsZero(1e-9))
            across = View.Camera.Direction.Cross(side);
        return across.Normalized();
    }

    /// <summary>Signed width: how far the cursor is from the first side, along the plane (or freely in 3D).</summary>
    private double Width()
    {
        if (_a is not { } a || _b is not { } b || Current is not { } inf)
            return 0;
        var side = (b - a).Normalized();
        var p = inf.Point;
        if (inf.Kind is InferenceKind.None)
        {
            // Free: the plane through the side facing the viewer.
            var ray = View.ScreenRay(Mouse);
            var view = View.Camera.Direction;
            var n = view - side * view.Dot(side);
            if (!_onFace && !n.IsZero(1e-9) && InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), n.Normalized(), a) is { } hit)
                p = hit;
        }
        var off = p - a;
        off -= side * off.Dot(side);
        var across = Across();
        // Off the plane (a snapped point above it), the rectangle tilts to reach it.
        return off.Length * Math.Sign(off.Dot(across) == 0 ? 1 : off.Dot(across));
    }

    /// <summary>The third and fourth corners for the cursor.</summary>
    private (Vec3 C, Vec3 D) Far(double width)
    {
        var a = _a!.Value;
        var b = _b!.Value;
        var side = (b - a).Normalized();
        var dir = Across();
        if (Current is { } inf)
        {
            var off = inf.Point - a;
            off -= side * off.Dot(side);
            if (!off.IsZero(1e-9) && !_onFace && inf.Kind is not InferenceKind.None)
                dir = off.Normalized() * Math.Sign(width == 0 ? 1 : width);
        }
        var w = dir * width;
        return (b + w, a + w);
    }

    private void Create(double width)
    {
        if (View.Document is not { } doc || Math.Abs(width) <= Tolerance.Length)
            return;
        var (c, d) = Far(width);
        Vec3[] corners = [_a!.Value, _b!.Value, c, d];
        var local = corners.Select(ToLocal).ToList();
        doc.Operation("Rectangle", e => StickyGeometry.DrawEdges(e, local, closed: true));
        _a = null;
        _b = null;
        ResetLocks();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (_a is not { } a || !Length.TryParse(text, LengthUnit.Millimeters, out var mm) || mm == 0)
            return false;
        if (_b == null)
        {
            if (Current is not { } inf)
                return false;
            var dir = (inf.Point - a).Normalized();
            if (dir.IsZero(1e-9))
                return false;
            _b = a + dir * mm;
            RefreshStatus();
            return true;
        }
        var sign = Width() < 0 ? -1 : 1;
        Create(sign * Math.Abs(mm));
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _a != null)
        {
            _a = null;
            _b = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_a is { } a && Current is { } inf)
        {
            if (_b is { } b)
            {
                var (c, d) = Far(Width());
                DrawWorldLine(overlay, a, b, Colors.Black, 1.5f);
                DrawWorldLine(overlay, b, c, Colors.Black, 1.5f);
                DrawWorldLine(overlay, c, d, Colors.Black, 1.5f);
                DrawWorldLine(overlay, d, a, Colors.Black, 1.5f);
            }
            else
            {
                DrawWorldLine(overlay, a, inf.Point, Colors.Black, 1.5f);
            }
        }
        DrawInference(overlay);
    }
}
