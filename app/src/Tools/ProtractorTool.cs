using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;
using System.Globalization;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Protractor: click the centre (the protractor lies on the face under the cursor or the axis plane
/// facing the viewer; arrows lock it), click the baseline, then click or type an angle. It places an infinite
/// guide line through the centre at that angle; Ctrl toggles guide creation (measure only). Angles snap every 15°.
/// </summary>
public sealed class ProtractorTool : DrawingTool
{
    private double SnapDegrees => View.Document?.Model.Options.AngleSnap ?? 15;
    private bool Snapping => View.Document?.Model.Options.AngleSnapping ?? true;

    private Vec3? _center;
    private Vec3 _normal = Vec3.UnitZ;
    private Vec3? _lockedNormal;
    private Vec3? _baseline;
    private bool _guides = true;

    public override int CommandId => CommandIds.Protractor;
    public override string CursorImage => "measure";
    protected override Vec3? From => _baseline == null ? _center : null;
    public override string VcbLabel => "Angle";

    public override string StatusText => (_center, _baseline) switch
    {
        (null, _) => "Click to set center of Protractor.",
        (_, null) => "Click to measure angle.",
        _ => _guides ? "Click to place guide or enter angle." : "Click to measure angle.",
    };

    public override string VcbValue => Angle() is { } a ? (a * 180 / Math.PI).ToString("F" + (View.Document?.Model.Options.AnglePrecision ?? 1), CultureInfo.InvariantCulture) : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_center == null)
        {
            _center = inf.Point;
            _normal = PlaneNormal(inf);
        }
        else if (_baseline == null)
        {
            var d = OnPlane(inf) - _center.Value;
            if (d.IsZero(1e-6))
                return;
            _baseline = d.Normalized();
        }
        else if (Angle() is { } angle)
        {
            Finish(doc, angle);
        }
        RefreshStatus();
    }

    /// <summary>The protractor lies on the face under the cursor, on the ground when the cursor is on the ground,
    /// otherwise on the axis plane facing the viewer; an arrow-key lock wins.</summary>
    private Vec3 PlaneNormal(InferenceResult inf) =>
        _lockedNormal ?? (inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal).Normalized()
            : inf.Kind == InferenceKind.InPlane ? Blue
            : DrawingPlane());

    private Vec3 OnPlane(InferenceResult inf)
    {
        var c = _center!.Value;
        var ray = View.ScreenRay(Mouse);
        if (inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace &&
            InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, c) is { } hit)
            return hit;
        return inf.Point - _normal * (inf.Point - c).Dot(_normal);
    }

    /// <summary>Signed angle from the baseline to the cursor, snapped to 15° steps nearby.</summary>
    private double? Angle()
    {
        if (_center is not { } c || _baseline is not { } s || Current is not { } inf)
            return null;
        var d = (OnPlane(inf) - c).Normalized();
        if (d.IsZero(1e-9))
            return null;
        var angle = Math.Atan2(s.Cross(d).Dot(_normal), s.Dot(d));
        var deg = angle * 180 / Math.PI;
        var snapped = Math.Round(deg / SnapDegrees) * SnapDegrees;
        if (Snapping && Math.Abs(deg - snapped) < 2 && inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace)
            angle = snapped * Math.PI / 180;
        return angle;
    }

    private void Finish(Document doc, double angle)
    {
        if (_guides)
        {
            var toLocal = doc.Context.ToWorld.Inverse();
            var dir = _baseline!.Value.RotatedAround(Vec3.Zero, _normal, angle);
            var point = toLocal.ApplyPoint(_center!.Value);
            var direction = toLocal.ApplyVector(dir).Normalized();
            doc.Operation("Guide", e => e.GuideLines.Add(new GuideLine(point, direction)));
        }
        _center = null;
        _baseline = null;
        ResetLocks();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (View.Document is not { } doc || _baseline == null)
            return false;
        var t = text.Trim();
        double degrees;
        if (t.Contains(':'))
        {
            // Slope "rise:run".
            var parts = t.Split(':');
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rise) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var run) || run == 0)
                return false;
            degrees = Math.Atan2(rise, run) * 180 / Math.PI;
        }
        else if (!double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
        {
            return false;
        }
        var sign = Angle() is { } a && a < 0 ? -1 : 1;
        Finish(doc, sign * Math.Abs(degrees) * Math.PI / 180);
        RefreshStatus();
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
            case Key.Escape when _center != null:
                _center = null;
                _baseline = null;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.Right or Key.Left or Key.Up when _center == null:
                var axis = key.Keycode == Key.Right ? Red : key.Keycode == Key.Left ? Green : Blue;
                _lockedNormal = _lockedNormal == axis ? null : axis;
                return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        var center = _center ?? Current?.Point;
        if (center is { } c)
        {
            // The protractor: a half disc in the plane, coloured by its normal axis, with 15° ticks.
            var normal = _center != null ? _normal : PlaneNormal(Current!);
            var color = AxisColor(IsAxis(normal) ? normal : null);
            var radius = 60 * View.Camera.WorldPerPixel(View.Size.Y, View.Camera.DepthOf(c));
            var (u, v) = Polygon.PlaneAxes(normal);
            if (_baseline is { } b)
            {
                u = b;
                v = normal.Cross(b).Normalized();
            }
            for (var i = 0; i <= 24; i++)
            {
                var a = Math.PI * i / 24;
                var dir = u * Math.Cos(a) + v * Math.Sin(a);
                var tick = i % 2 == 0 ? 0.85 : 0.92;
                DrawWorldLine(overlay, c + dir * (radius * tick), c + dir * radius, color, 1);
                if (i < 24)
                {
                    var next = u * Math.Cos(Math.PI * (i + 1) / 24) + v * Math.Sin(Math.PI * (i + 1) / 24);
                    DrawWorldLine(overlay, c + dir * radius, c + next * radius, color, 1.5f);
                }
            }
            if (_baseline is { } baseline && Current is { } inf)
            {
                DrawWorldLine(overlay, c, c + baseline * radius * 1.5, Colors.Black, 1, dashed: true);
                DrawWorldLine(overlay, c, OnPlane(inf), Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}
