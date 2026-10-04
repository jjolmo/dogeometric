using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Freehand: press and drag to draw a curve on the plane where the drag starts (the face under the
/// cursor, else the ground); release to finish. Ending on the start point closes it into a face.
/// </summary>
public sealed class FreehandTool : DrawingTool
{
    // A new point every few pixels of drag, like SketchUp's freehand density.
    private const float StepPixels = 6;

    private readonly List<Vec3> _points = [];
    private Vec3 _normal = Vec3.UnitZ;
    private Vector2 _lastScreen;

    public override int CommandId => CommandIds.Freehand;
    public override string CursorImage => "freehand";
    public override string StatusText => "Click and drag to draw freehand curve.";

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        _normal = inf.Face is { } f ? inf.EntityToWorld.ApplyNormal(f.Normal).Normalized()
            : inf.Kind == InferenceKind.InPlane ? Blue : MostFacingPlane();
        _points.Clear();
        _points.Add(inf.Point);
        _lastScreen = position;
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        base.MouseMove(position, relative);
        if (_points.Count == 0 || position.DistanceTo(_lastScreen) < StepPixels)
            return;
        var ray = View.ScreenRay(position);
        if (InferenceEngine.IntersectPlane(new Core.Picking.Ray(ray.Origin, ray.Direction), _normal, _points[0]) is { } p)
        {
            _points.Add(p);
            _lastScreen = position;
            View.QueueOverlayRedraw();
        }
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _points.Count < 2 || View.Document is not { } doc)
        {
            _points.Clear();
            return;
        }
        var points = _points.ToList();
        _points.Clear();
        // Ending near the start (on screen) closes the curve.
        var closed = points.Count > 3 && View.ToScreen(points[0]) is { } s0 && View.ToScreen(points[^1]) is { } s1 && s0.DistanceTo(s1) < 10;
        if (closed)
            points.RemoveAt(points.Count - 1);
        var local = points.Select(ToLocal).ToList();
        var normal = doc.Context.ToWorld.Inverse().ApplyVector(_normal).Normalized();
        doc.Operation("Freehand", e => StickyGeometry.DrawEdges(e, local, closed, normal, new Core.Modeling.Curve { Segments = local.Count, IsPolygon = false }));
        View.QueueOverlayRedraw();
    }

    public override void Draw(Control overlay)
    {
        for (var i = 0; i + 1 < _points.Count; i++)
            DrawWorldLine(overlay, _points[i], _points[i + 1], Colors.Black, 1.5f);
        if (_points.Count == 0)
            DrawInference(overlay);
    }
}
