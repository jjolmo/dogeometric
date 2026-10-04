using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using System.Globalization;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Arc (and Pie): click the centre (on the face under the cursor, else the ground or the axis plane
/// facing the viewer), click the start (sets the radius, or type it), then click or type the angle; angles snap
/// every 15°. Pie closes the arc to the centre into a face.
/// </summary>
public class CenterArcTool(bool pie) : DrawingTool
{
    private const double SnapDegrees = 15;

    private Vec3? _center;
    private Vec3? _start;
    private Vec3 _normal = Vec3.UnitZ;

    public override int CommandId => pie ? CommandIds.Pie : CommandIds.Arc;
    public override string CursorImage => "arc1";
    protected override Vec3? From => _center;
    public override string VcbLabel => _start == null ? "Radius" : "Angle";
    public override string StatusText => (_center, _start) switch
    {
        (null, _) => "Select center point.",
        (_, null) => "Select start point or enter radius.",
        _ => "Select end point or enter angle.",
    };

    public override string VcbValue => (_center, _start, Current) switch
    {
        ({ } c, null, { } inf) => Length.Format(c.DistanceTo(inf.Point), LengthUnit.Millimeters, 1),
        ({ }, { }, _) when Sweep() is { } a => (a * 180 / Math.PI).ToString("0.0", CultureInfo.InvariantCulture),
        _ => "",
    };

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_center == null)
        {
            _center = inf.Point;
            _normal = inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal).Normalized()
                : inf.Kind == InferenceKind.InPlane ? Blue : MostFacingPlane();
        }
        else if (_start == null)
        {
            var p = OnPlane(inf);
            if (p.DistanceTo(_center.Value) <= Tolerance.Length)
                return;
            _start = p;
        }
        else if (Sweep() is { } sweep)
        {
            Create(sweep);
        }
        RefreshStatus();
    }

    private Vec3 OnPlane(InferenceResult inf)
    {
        var c = _center!.Value;
        var ray = View.ScreenRay(Mouse);
        if (inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace &&
            InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, c) is { } hit)
            return hit;
        return inf.Point - _normal * (inf.Point - c).Dot(_normal);
    }

    /// <summary>Angle from the start to the cursor about the normal (0..2π), snapped to 15° steps nearby.</summary>
    private double? Sweep()
    {
        if (_center is not { } c || _start is not { } s || Current is not { } inf)
            return null;
        var u = (s - c).Normalized();
        var v = _normal.Cross(u);
        var d = OnPlane(inf) - c;
        if (d.IsZero(1e-9))
            return null;
        var a = Math.Atan2(d.Dot(v), d.Dot(u));
        if (a <= 0)
            a += 2 * Math.PI;
        var deg = a * 180 / Math.PI;
        var snapped = Math.Round(deg / SnapDegrees) * SnapDegrees;
        if (Math.Abs(deg - snapped) < 2)
            a = snapped * Math.PI / 180;
        return a;
    }

    private void Create(double sweep)
    {
        if (View.Document is not { } doc || _center is not { } c || _start is not { } s || sweep <= 1e-9)
            return;
        var segments = Math.Max(1, (int)Math.Round(Shapes.DefaultArcSegments * sweep / Math.PI));
        var points = Shapes.CenterArc(c, _normal, s, sweep, segments).Select(ToLocal).ToList();
        var center = ToLocal(c);
        var normal = doc.Context.ToWorld.Inverse().ApplyVector(_normal).Normalized();
        doc.Operation(pie ? "Pie" : "Arc", e =>
        {
            var curve = new Curve { Center = center, Normal = normal, Radius = c.DistanceTo(s), Segments = segments };
            StickyGeometry.DrawEdges(e, points, closed: false, normal, curve);
            if (pie)
                StickyGeometry.DrawEdges(e, [points[^1], center, points[0]], closed: false, normal);
        });
        _center = null;
        _start = null;
        ResetLocks();
        View.QueueOverlayRedraw();
    }

    public override bool ApplyVcb(string text)
    {
        if (_center is not { } c)
            return false;
        if (_start == null)
        {
            if (Current is not { } inf || !Length.TryParse(text, LengthUnit.Millimeters, out var r) || r <= 0)
                return false;
            var dir = (OnPlane(inf) - c).Normalized();
            if (dir.IsZero(1e-9))
                return false;
            _start = c + dir * r;
            RefreshStatus();
            return true;
        }
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var deg) || deg == 0)
            return false;
        var a = deg * Math.PI / 180;
        Create(a < 0 ? a + 2 * Math.PI : a);
        RefreshStatus();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _center != null)
        {
            _center = null;
            _start = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_center is { } c)
        {
            if (_start is { } s)
            {
                if (Sweep() is { } sweep)
                {
                    var pts = Shapes.CenterArc(c, _normal, s, sweep, Math.Max(2, (int)(sweep * 12)));
                    for (var i = 0; i + 1 < pts.Count; i++)
                        DrawWorldLine(overlay, pts[i], pts[i + 1], Colors.Black, 1.5f);
                    if (pie)
                    {
                        DrawWorldLine(overlay, c, pts[0], Colors.Black, 1.5f);
                        DrawWorldLine(overlay, c, pts[^1], Colors.Black, 1.5f);
                    }
                }
                DrawWorldLine(overlay, c, s, Colors.Black, 1, dashed: true);
            }
            else if (Current is { } inf)
            {
                DrawWorldLine(overlay, c, OnPlane(inf), Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}

public sealed class PieTool() : CenterArcTool(true);

public sealed class ThreePointArcTool : DrawingTool
{
    private readonly List<Vec3> _points = [];

    public override int CommandId => CommandIds.Arc3Point;
    public override string CursorImage => "arc3point1";
    protected override Vec3? From => _points.Count > 0 ? _points[^1] : null;
    public override string StatusText => _points.Count switch
    {
        0 => "Select start point.",
        1 => "Select second point on the arc.",
        _ => "Select end point.",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_points.Count > 0 && inf.Point.DistanceTo(_points[^1]) <= Tolerance.Length)
            return;
        _points.Add(inf.Point);
        if (_points.Count == 3)
        {
            if (Shapes.ThreePointArc(_points[0], _points[1], _points[2], Shapes.DefaultArcSegments) is { } arc)
            {
                var local = arc.Select(ToLocal).ToList();
                var normal = doc.Context.ToWorld.Inverse().ApplyVector((_points[1] - _points[0]).Cross(_points[2] - _points[0])).Normalized();
                doc.Operation("3 Point Arc", e => StickyGeometry.DrawEdges(e, local, closed: false, normal, new Curve { Normal = normal, Segments = local.Count - 1 }));
            }
            _points.Clear();
            ResetLocks();
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _points.Count > 0)
        {
            _points.Clear();
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (Current is { } inf)
        {
            if (_points.Count == 1)
                DrawWorldLine(overlay, _points[0], inf.Point, Colors.Black, 1, dashed: true);
            else if (_points.Count == 2 && Shapes.ThreePointArc(_points[0], _points[1], inf.Point, 24) is { } arc)
                for (var i = 0; i + 1 < arc.Count; i++)
                    DrawWorldLine(overlay, arc[i], arc[i + 1], Colors.Black, 1.5f);
        }
        DrawInference(overlay);
    }
}
