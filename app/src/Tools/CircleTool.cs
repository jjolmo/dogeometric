using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;
using Curve = Dogeometric.Core.Modeling.Curve;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Circle and Polygon tools: click the centre (on the face under the cursor, or the axis plane facing
/// the viewer; arrow keys lock the normal), click the radius. The Measurements box takes the radius, or
/// "48s" for the number of segments/sides. Ctrl toggles inscribed/circumscribed polygons.
/// </summary>
public class CircleTool(bool polygon) : DrawingTool
{
    private static int _circleSegments = Shapes.DefaultCircleSegments;
    private static int _polygonSides = Shapes.DefaultPolygonSides;

    private Vec3? _center;
    private Vec3 _normal = Vec3.UnitZ;
    private Vec3? _lockedNormal;
    private bool _circumscribed;

    public CircleTool() : this(false) { }

    public override int CommandId => polygon ? CommandIds.Polygon : CommandIds.Circle;
    public override string CursorImage => polygon ? "polygon" : "circle";
    public override string VcbLabel => _center == null ? (polygon ? "Sides" : "Segments") : "Radius";
    public override string StatusText => _center == null
        ? "Click to set center."
        : polygon ? (_circumscribed ? "Click to finish.  Ctrl = inscribed." : "Click to finish.  Ctrl = circumscribed.") : "Click to finish.";

    private int Segments
    {
        get => polygon ? _polygonSides : _circleSegments;
        set
        {
            if (polygon)
                _polygonSides = value;
            else
                _circleSegments = value;
        }
    }

    public override string VcbValue => _center is { } c && Current is { } p
        ? UI.Measure.Show(PlanePoint(p).DistanceTo(c))
        : Segments.ToString();

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void Activate() => View.ShowVcbValue(Segments.ToString());

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_center == null)
        {
            _center = inf.Point;
            _normal = _lockedNormal ?? (inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal) : MostFacingPlane());
            RefreshStatus();
            return;
        }
        var edge = PlanePoint(inf);
        Create(_center.Value.DistanceTo(edge), edge - _center.Value);
    }

    private Vec3 PlanePoint(InferenceResult inf)
    {
        if (_center is not { } c)
            return inf.Point;
        var ray = View.ScreenRay(Mouse);
        if (inf.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace &&
            InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, c) is { } hit)
            return hit;
        return inf.Point - _normal * (inf.Point - c).Dot(_normal);
    }

    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.EndsWith('s') && int.TryParse(t[..^1], out var n) || (_center == null && int.TryParse(t, out n)))
        {
            if (n < 3 || n > 999)
                return false;
            Segments = n;
            View.ShowVcbValue(VcbValue);
            return true;
        }
        if (_center is not { } c || Current is not { } inf || !UI.Measure.Read(t, out var r) || r <= 0)
            return false;
        var dir = PlanePoint(inf) - c;
        Create(r, dir.IsZero(1e-9) ? Polygon.PlaneAxes(_normal).U : dir);
        return true;
    }

    private void Create(double radius, Vec3 startDir)
    {
        if (_center is not { } c || radius <= Tolerance.Length || View.Document is not { } doc)
            return;
        var pts = Shapes.RegularPolygon(c, _normal, startDir, radius, Segments, polygon && _circumscribed);
        var toLocal = doc.Context.ToWorld.Inverse();
        var local = pts.Select(toLocal.ApplyPoint).ToList();
        var normal = toLocal.ApplyNormal(_normal);
        var curve = new Curve { Center = toLocal.ApplyPoint(c), Normal = normal, Radius = radius, Segments = Segments, IsPolygon = polygon };
        doc.Operation(polygon ? "Polygon" : "Circle", e => StickyGeometry.DrawEdges(e, local, closed: true, normal, curve));
        _center = null;
        RefreshStatus();
        UpdateInference();
    }

    public override bool KeyDown(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Escape when _center != null:
                _center = null;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            case Key.Ctrl when polygon && !key.Echo:
                _circumscribed = !_circumscribed;
                RefreshStatus();
                View.QueueOverlayRedraw();
                return true;
            // Before the first click, arrows lock the circle's normal (→ red, ← green, ↑ blue).
            case Key.Right or Key.Left or Key.Up when _center == null:
                var axis = key.Keycode == Key.Right ? Red : key.Keycode == Key.Left ? Green : Blue;
                _lockedNormal = _lockedNormal == axis ? null : axis;
                View.QueueOverlayRedraw();
                return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_center is { } c && Current is { } inf)
        {
            var edge = PlanePoint(inf);
            var r = c.DistanceTo(edge);
            if (r > Tolerance.Length)
            {
                var pts = Shapes.RegularPolygon(c, _normal, edge - c, r, Segments, polygon && _circumscribed);
                for (var i = 0; i < pts.Count; i++)
                    DrawWorldLine(overlay, pts[i], pts[(i + 1) % pts.Count], Colors.Black, 1.5f);
                DrawWorldLine(overlay, c, edge, Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}

public sealed class PolygonTool() : CircleTool(true);
