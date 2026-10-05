using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Axes tool: click the new origin, then a point along the red axis, then one towards green; blue
/// completes a right-handed set. Alt switches to the alternate orientation (green first, then red). Inference,
/// arrow-key locks and the ground plane follow the new axes. Given a component (context menu › Change Axes), the
/// axes become that component's instead.
/// </summary>
public sealed class AxesTool(ComponentInstance? component = null) : DrawingTool
{
    private Vec3? _origin;
    private Vec3? _red;
    private bool _alternate;

    public override int CommandId => component == null ? CommandIds.Axes : 0;
    protected override Vec3? From => _origin;

    public override string StatusText => (_origin, _red) switch
    {
        (null, _) => "Pick point for origin of axes.  Alt = Alternate axis orientation",
        (_, null) => (_alternate ? "Pick direction for green axis." : "Pick direction for red axis.") + "  Alt = Alternate axis orientation",
        _ => _alternate ? "Pick direction for red axis." : "Pick direction for green axis.",
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
            var first = _red.Value;
            var g = inf.Point - _origin.Value;
            g -= first * g.Dot(first);
            if (g.IsZero(Tolerance.Length))
                return;
            var second = g.Normalized();
            // In the alternate orientation the first pick was green and the second red.
            var (red, green) = _alternate ? (second, first) : (first, second);
            var axes = new Transform(red, green, red.Cross(green).Normalized(), _origin.Value);
            if (component != null)
            {
                var inParent = axes.Then(doc.Context.ToWorld.Inverse());
                var collections = doc.Model.Definitions.Select(d => d.Entities).Append(doc.Model.Entities).ToArray();
                doc.Undo.Begin("Change Axes", collections);
                Grouping.ChangeAxes(doc.Model, component, inParent);
                doc.Undo.Commit();
                Manager.Activate(new SelectTool());
                return;
            }
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
        if (key.Keycode == Key.Alt && !key.Echo && _red == null)
        {
            _alternate = !_alternate;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
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
            var (redColor, greenColor) = (new Color(0.86f, 0, 0), new Color(0, 0.62f, 0));
            if (_red is { } first)
            {
                DrawWorldLine(overlay, o, o + first * scale, _alternate ? greenColor : redColor, 2);
                var g = inf.Point - o;
                g -= first * g.Dot(first);
                if (!g.IsZero(1e-9))
                {
                    g = g.Normalized();
                    DrawWorldLine(overlay, o, o + g * scale, _alternate ? redColor : greenColor, 2);
                    var (r, gr) = _alternate ? (g, first) : (first, g);
                    DrawWorldLine(overlay, o, o + r.Cross(gr).Normalized() * scale, new Color(0, 0, 0.86f), 2);
                }
            }
            else
            {
                DrawWorldLine(overlay, o, inf.Point, _alternate ? greenColor : redColor, 2);
            }
        }
        DrawInference(overlay);
    }
}
