using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Axes tool: click the new origin, then a point along the red axis, then one towards green; blue
/// completes a right-handed set. Inference, arrow-key locks and the ground plane follow the new axes.
/// </summary>
public sealed class AxesTool : DrawingTool
{
    private Vec3? _origin;
    private Vec3? _red;

    public override int CommandId => CommandIds.Axes;
    protected override Vec3? From => _origin;

    public override string StatusText => (_origin, _red) switch
    {
        (null, _) => "Select point for origin.",
        (_, null) => "Select point on red axis.",
        _ => "Select point on green axis.",
    };

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        if (_origin == null)
        {
            _origin = inf.Point;
        }
        else if (_red == null)
        {
            var d = inf.Point - _origin.Value;
            if (d.IsZero(Tolerance.Length))
                return;
            _red = d.Normalized();
        }
        else
        {
            var red = _red.Value;
            var g = inf.Point - _origin.Value;
            g -= red * g.Dot(red);
            if (g.IsZero(Tolerance.Length))
                return;
            var green = g.Normalized();
            var axes = new Transform(red, green, red.Cross(green).Normalized(), _origin.Value);
            doc.Operation("Place Axes", _ => doc.Model.Axes = axes);
            _origin = null;
            _red = null;
            ResetLocks();
        }
        RefreshStatus();
        View.QueueOverlayRedraw();
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _origin != null)
        {
            _origin = null;
            _red = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (_origin is { } o && Current is { } inf)
        {
            var scale = 80 * View.Camera.WorldPerPixel(View.Size.Y, View.Camera.DepthOf(o));
            if (_red is { } r)
            {
                DrawWorldLine(overlay, o, o + r * scale, new Color(0.86f, 0, 0), 2);
                var g = inf.Point - o;
                g -= r * g.Dot(r);
                if (!g.IsZero(1e-9))
                {
                    g = g.Normalized();
                    DrawWorldLine(overlay, o, o + g * scale, new Color(0, 0.62f, 0), 2);
                    DrawWorldLine(overlay, o, o + r.Cross(g).Normalized() * scale, new Color(0, 0, 0.86f), 2);
                }
            }
            else
            {
                DrawWorldLine(overlay, o, inf.Point, new Color(0.86f, 0, 0), 2);
            }
        }
        DrawInference(overlay);
    }
}
