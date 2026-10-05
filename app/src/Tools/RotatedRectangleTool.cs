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
/// on the plane facing the viewer through the first side; Alt locks that plane (Shift unlocks), and after the first
/// corner Alt sets the baseline typed angles turn from. The width snaps to a square and to the golden section.
/// Typed: "length" or "length,angle" for the side, "width" or "width,angle" (tilt from the plane) for the rest.
/// </summary>
public sealed class RotatedRectangleTool : DrawingTool
{
    private static readonly double Golden = (1 + Math.Sqrt(5)) / 2;

    private Vec3? _a;
    private Vec3? _b;
    private Vec3 _faceNormal;
    private bool _onFace;
    private Vec3? _lockedNormal;
    private Vec3? _baseline;
    private string? _snap;

    public override int CommandId => CommandIds.RotatedRectangle;
    public override string CursorImage => "rectangle";
    protected override Vec3? From => _b ?? _a;
    public override string VcbLabel => _b == null ? "Length" : "Width";
    public override string StatusText => (_a, _b) switch
    {
        (null, _) => "Select first corner." + (_lockedNormal == null ? "  Alt = lock protractor plane." : "  Shift = unlock."),
        (_, null) => "Select second corner or enter value(s).  Alt = set protractor baseline.",
        _ => "Select third corner or enter value(s).",
    };

    public override string VcbValue => (_a, _b, Current) switch
    {
        ({ } a, null, { } c) => UI.Measure.Show(a.DistanceTo(c.Point)),
        ({ }, { }, { }) => UI.Measure.Show(Math.Abs(Width())),
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
            _onFace = _lockedNormal != null || inf.Face != null;
            if (_lockedNormal is { } locked)
                _faceNormal = locked;
            else if (inf.Face is { } f)
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
        var width = off.Length * Math.Sign(off.Dot(across) == 0 ? 1 : off.Dot(across));
        _snap = null;
        if (inf.Kind is InferenceKind.None or InferenceKind.OnFace or InferenceKind.InPlane)
        {
            // Square and golden-section widths snap within a few pixels, as SketchUp's tooltips show.
            var length = a.DistanceTo(b);
            var tolerance = 6 * View.Camera.WorldPerPixel(View.Size.Y, View.Camera.DepthOf(p));
            foreach (var (target, label) in new[] { (length, "Square"), (length / Golden, "Golden Section"), (length * Golden, "Golden Section") })
                if (Math.Abs(Math.Abs(width) - target) < tolerance)
                {
                    width = Math.Sign(width == 0 ? 1 : width) * target;
                    _snap = label;
                    break;
                }
        }
        return width;
    }

    /// <summary>The plane the rectangle starts on: the locked one, the face clicked, or the plane facing the viewer.</summary>
    private Vec3 PlaneNormal(InferenceResult? inf) =>
        _a != null ? (_onFace ? _faceNormal : MostFacingPlane())
        : _lockedNormal ?? (inf?.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal).Normalized() : MostFacingPlane());

    /// <summary>Where typed angles turn from: the baseline set with Alt, else the plane's first axis.</summary>
    private Vec3 Baseline(Vec3 normal)
    {
        if (_baseline is { } b)
            return b;
        var red = Vec3.UnitX - normal * Vec3.UnitX.Dot(normal);
        return red.IsZero(1e-6) ? Texturing.PlaneAxes(normal).X : red.Normalized();
    }

    private static Vec3 Turn(Vec3 v, Vec3 axis, double degrees) => Transform.Rotation(axis, degrees * Math.PI / 180).ApplyVector(v);

    private static bool TryValues(string text, out double first, out double? angle)
    {
        var parts = text.Split(',', ';');
        angle = null;
        first = 0;
        if (parts.Length is < 1 or > 2 || !UI.Measure.Read(parts[0], out first))
            return false;
        if (parts.Length == 2)
        {
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a))
                return false;
            angle = a;
        }
        return true;
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

    private void Create(double width, double? tilt = null)
    {
        if (View.Document is not { } doc || Math.Abs(width) <= Tolerance.Length)
            return;
        var (c, d) = Far(width);
        if (tilt is { } degrees)
        {
            // Tilted from the starting plane about the first side.
            var side = (_b!.Value - _a!.Value).Normalized();
            var w = Turn(Across(), side, degrees) * width;
            (c, d) = (_b.Value + w, _a.Value + w);
        }
        Vec3[] corners = [_a!.Value, _b!.Value, c, d];
        var local = corners.Select(ToLocal).ToList();
        doc.Operation("Rectangle", e => StickyGeometry.DrawEdges(e, local, closed: true));
        _a = null;
        _b = null;
        _baseline = null;
        ResetLocks();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (_a is not { } a || !TryValues(text, out var mm, out var angle) || mm == 0)
            return false;
        if (_b == null)
        {
            if (Current is not { } inf)
                return false;
            var normal = PlaneNormal(inf);
            var dir = angle is { } degrees ? Turn(Baseline(normal), normal, degrees) : (inf.Point - a).Normalized();
            if (dir.IsZero(1e-9))
                return false;
            _b = a + dir * mm;
            RefreshStatus();
            return true;
        }
        var sign = Width() < 0 ? -1 : 1;
        Create(sign * Math.Abs(mm), angle);
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Alt && !key.Echo && Current is { } inf)
        {
            if (_a == null)
                _lockedNormal = PlaneNormal(inf);
            else if (_b == null)
            {
                var normal = PlaneNormal(inf);
                var off = inf.Point - _a.Value;
                off -= normal * off.Dot(normal);
                if (!off.IsZero(1e-9))
                    _baseline = off.Normalized();
            }
            RefreshStatus();
            return true;
        }
        if (key.Keycode == Key.Shift && !key.Echo && (_lockedNormal != null || _baseline != null))
        {
            _lockedNormal = null;
            _baseline = null;
            RefreshStatus();
            return true;
        }
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
                if (_snap is { } label && View.ToScreen(c) is { } at)
                {
                    DrawTooltip(overlay, at, label);
                    return;
                }
            }
            else
            {
                DrawWorldLine(overlay, a, inf.Point, Colors.Black, 1.5f);
            }
        }
        DrawInference(overlay);
    }
}
