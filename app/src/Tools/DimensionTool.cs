using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Dimension: click two points (or an edge), then pull the dimension line out and click to place it.
/// The offset snaps to the red, green or blue direction when close to one.
/// </summary>
public sealed class DimensionTool : DrawingTool
{
    private Vec3? _start;
    private Vec3? _end;

    public override int CommandId => CommandIds.Dimension;
    public override string CursorImage => "dimension";
    protected override Vec3? From => _end == null ? _start : null;

    public override string StatusText => (_start, _end) switch
    {
        (null, _) => "Select an edge, curve, or two points to dimension, or drag one to move.",
        (_, null) => "Select second point for linear dimension.",
        _ => "Select position for dimension.",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_start == null)
        {
            if (inf is { Kind: InferenceKind.OnEdge, Edge: { } edge })
            {
                // An edge dimensions its own length.
                _start = inf.EntityToWorld.ApplyPoint(edge.Start.Position);
                _end = inf.EntityToWorld.ApplyPoint(edge.End.Position);
            }
            else
            {
                _start = inf.Point;
            }
        }
        else if (_end == null)
        {
            if (inf.Point.DistanceTo(_start.Value) <= Tolerance.Length)
                return;
            _end = inf.Point;
        }
        else
        {
            Place(doc);
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    /// <summary>Offset of the dimension line from the measured points, for the cursor.</summary>
    private Vec3 Offset()
    {
        var s = _start!.Value;
        var e = _end!.Value;
        var axis = (e - s).Normalized();
        // The cursor on the plane through the measured segment that faces the viewer most.
        var view = View.Camera.Direction;
        var normal = view - axis * view.Dot(axis);
        var ray = View.ScreenRay(Mouse);
        Vec3 point;
        if (Current is { Kind: InferenceKind.Endpoint or InferenceKind.Midpoint or InferenceKind.Center or InferenceKind.OnEdge } snap)
            point = snap.Point;
        else if (!normal.IsZero(1e-9) && InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), normal.Normalized(), s) is { } hit)
            point = hit;
        else
            return Vec3.Zero;
        var offset = point - s;
        offset -= axis * offset.Dot(axis);
        // Snap to an axis direction perpendicular to the segment.
        foreach (var a in new[] { Red, Green, Blue })
        {
            if (Math.Abs(a.Dot(axis)) > 1e-6 || offset.IsZero(1e-9))
                continue;
            var cos = Math.Abs(offset.Normalized().Dot(a));
            if (cos > Math.Cos(10 * Math.PI / 180))
                return a * offset.Dot(a);
        }
        return offset;
    }

    private void Place(Document doc)
    {
        var toLocal = doc.Context.ToWorld.Inverse();
        var s = toLocal.ApplyPoint(_start!.Value);
        var e = toLocal.ApplyPoint(_end!.Value);
        var offset = toLocal.ApplyVector(Offset());
        doc.Operation("Dimension", ent => ent.Dimensions.Add(new LinearDimension(s, e, offset)));
        _start = null;
        _end = null;
        ResetLocks();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _start != null)
        {
            _start = null;
            _end = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_start is { } s)
        {
            if (_end is { } e)
            {
                // Preview of the dimension being placed.
                var o = Offset();
                DrawWorldLine(overlay, s, s + o, Colors.Black, 1);
                DrawWorldLine(overlay, e, e + o, Colors.Black, 1);
                DrawWorldLine(overlay, s + o, e + o, Colors.Black, 1);
                if (View.ToScreen((s + e) * 0.5 + o) is { } mid)
                {
                    var model = View.Document!.Model;
                    var text = Core.Units.Length.Format(s.DistanceTo(e), model.Units, model.UnitPrecision);
                    var font = overlay.GetThemeDefaultFont();
                    var size = font.GetStringSize(text, HorizontalAlignment.Left, -1, 13);
                    overlay.DrawString(font, mid - new Vector2(size.X / 2, 4), text, HorizontalAlignment.Left, -1, 13, Colors.Black);
                }
            }
            else if (Current is { } inf)
            {
                DrawWorldLine(overlay, s, inf.Point, Colors.Black, 1, dashed: true);
            }
        }
        DrawInference(overlay);
    }
}
