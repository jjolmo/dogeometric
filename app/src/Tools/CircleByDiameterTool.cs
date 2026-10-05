using Dogeometric.App.Commands;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>
/// CircleByDiameter (The Sketchup Dude): click one end of the diameter, then the other; both ends become vertices of
/// the circle. The Measurements box takes the diameter, or "24s" for the number of segments.
/// </summary>
public sealed class CircleByDiameterTool : DrawingTool
{
    private static int _segments = Shapes.DefaultCircleSegments;

    private Vec3? _start;
    private Vec3 _normal = Vec3.UnitZ;
    private Vec3? _lockedNormal;

    public override int CommandId => ExtensionIds.CircleByDiameter;
    public override string CursorImage => "circle";
    public override string VcbLabel => _start == null ? "Segments" : "Diameter";
    public override string StatusText => _start == null ? "Click the first end of the diameter." : "Click the other end of the diameter.";
    protected override Vec3? From => _start;

    public override string VcbValue => _start is { } s && Current is { } p
        ? UI.Measure.Show(PlanePoint(p).DistanceTo(s))
        : _segments.ToString();

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void Activate() => View.ShowVcbValue(_segments.ToString());

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_start == null)
        {
            _start = inf.Point;
            _normal = _lockedNormal ?? (inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal) : MostFacingPlane());
            RefreshStatus();
            return;
        }
        Create(PlanePoint(inf));
    }

    private Vec3 PlanePoint(InferenceResult inf)
    {
        if (_start is not { } s)
            return inf.Point;
        var ray = View.ScreenRay(Mouse);
        if (inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace &&
            InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, s) is { } hit)
            return hit;
        return inf.Point - _normal * (inf.Point - s).Dot(_normal);
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.EndsWith('s') && int.TryParse(t[..^1], out var n) || (_start == null && int.TryParse(t, out n)))
        {
            if (n < 3 || n > 999)
                return false;
            _segments = n;
            View.ShowVcbValue(VcbValue);
            return true;
        }
        if (_start is not { } s || Current is not { } inf || !UI.Measure.Read(t, out var d) || d <= 0)
            return false;
        var dir = PlanePoint(inf) - s;
        if (dir.IsZero(1e-9))
            dir = Polygon.PlaneAxes(_normal).U;
        Create(s + dir.Normalized() * d);
        return true;
    }

    private void Create(Vec3 end)
    {
        if (_start is not { } s || View.Document is not { } doc || s.DistanceTo(end) <= Tolerance.Length)
            return;
        var centre = (s + end) / 2;
        var radius = s.DistanceTo(end) / 2;
        // Starting at the first end puts both ends on vertices (with an even number of segments).
        var pts = Shapes.RegularPolygon(centre, _normal, s - centre, radius, _segments, false);
        var toLocal = doc.Context.ToWorld.Inverse();
        var normal = toLocal.ApplyNormal(_normal);
        var curve = new Curve { Center = toLocal.ApplyPoint(centre), Normal = normal, Radius = radius, Segments = _segments };
        doc.Operation("Circle", e => StickyGeometry.DrawEdges(e, pts.Select(toLocal.ApplyPoint).ToList(), closed: true, normal, curve));
        _start = null;
        RefreshStatus();
        UpdateInference();
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Escape when _start != null:
                _start = null;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.Right or Key.Left or Key.Up when _start == null:
                var axis = key.Keycode == Key.Right ? Red : key.Keycode == Key.Left ? Green : Blue;
                _lockedNormal = _lockedNormal == axis ? null : axis;
                return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_start is { } s && Current is { } inf)
        {
            var end = PlanePoint(inf);
            if (s.DistanceTo(end) > Tolerance.Length)
            {
                var centre = (s + end) / 2;
                var pts = Shapes.RegularPolygon(centre, _normal, s - centre, s.DistanceTo(end) / 2, _segments, false);
                for (var i = 0; i < pts.Count; i++)
                    DrawWorldLine(overlay, pts[i], pts[(i + 1) % pts.Count], Colors.Black, 1.5f);
                DrawWorldLine(overlay, s, end, Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}
