using Dogeometric.App.Viewport;
using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// Base for tools that pick points with inference (Line, Rectangle, Tape Measure…): tracks the snapped point,
/// draws SketchUp's inference markers and tooltips, and handles axis locks (arrow keys) and Shift-lock.
/// </summary>
public abstract class DrawingTool : Tool
{
    protected readonly InferenceEngine Inference = new();
    protected InferenceResult? Current { get; private set; }
    protected Vector2 Mouse { get; private set; }

    /// <summary>Point the axis inference measures from (the previous click), or null before the first click.</summary>
    protected virtual Vec3? From => null;

    public override Input.CursorShape Cursor => Input.CursorShape.Cross;

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        Mouse = position;
        UpdateInference();
    }

    protected void UpdateInference()
    {
        if (View.Document is not { } doc)
            return;
        Current = Inference.Infer(new ViewProjection(View), Mouse.X, Mouse.Y, From, doc.Context.Entities, doc.Context.ToWorld);
        OnInferenceChanged();
        View.QueueOverlayRedraw();
    }

    protected virtual void OnInferenceChanged() { }

    public override bool KeyDown(InputEventKey key)
    {
        // Arrow keys toggle an axis lock (→ red, ← green, ↑ blue), like SketchUp.
        Vec3? axis = key.Keycode switch
        {
            Key.Right => Vec3.UnitX,
            Key.Left => Vec3.UnitY,
            Key.Up => Vec3.UnitZ,
            _ => null,
        };
        if (axis is { } a && From != null)
        {
            Inference.LockedAxis = Inference.LockedAxis == a ? null : a;
            UpdateInference();
            return true;
        }
        if (key.Keycode == Key.Down && From != null)
        {
            Inference.LockedAxis = null;
            Inference.LockedLine = null;
            UpdateInference();
            return true;
        }
        // Holding Shift keeps the current axis inference.
        if (key.Keycode == Key.Shift && Current is { Kind: InferenceKind.OnAxis, AxisFrom: { } f, AxisDirection: { } d })
        {
            Inference.LockedLine = (f, d, Current.Label);
            return true;
        }
        return false;
    }

    public override bool KeyUp(InputEventKey key)
    {
        if (key.Keycode == Key.Shift && Inference.LockedLine != null)
        {
            Inference.LockedLine = null;
            UpdateInference();
            return true;
        }
        return false;
    }

    protected void ResetLocks()
    {
        Inference.LockedAxis = null;
        Inference.LockedLine = null;
    }

    /// <summary>World point → active context's local point (where new geometry goes).</summary>
    protected Vec3 ToLocal(Vec3 world) => View.Document!.Context.ToWorld.Inverse().ApplyPoint(world);

    // ------------------------------------------------------------------ feedback drawing

    public static Color AxisColor(Vec3? dir) => dir switch
    {
        { } d when Math.Abs(d.X) > 0.99 => new Color(0.86f, 0, 0),
        { } d when Math.Abs(d.Y) > 0.99 => new Color(0, 0.62f, 0),
        { } d when Math.Abs(d.Z) > 0.99 => new Color(0, 0, 0.86f),
        { } => new Color(0.86f, 0, 0.86f),
        _ => Colors.Black,
    };

    /// <summary>Line in model space drawn on the overlay (rubber band).</summary>
    protected void DrawWorldLine(Control overlay, Vec3 a, Vec3 b, Color color, float width = 1.5f, bool dashed = false)
    {
        if (View.ToScreen(a) is not { } sa || View.ToScreen(b) is not { } sb)
            return;
        if (dashed)
            overlay.DrawDashedLine(sa, sb, color, width, 5);
        else
            overlay.DrawLine(sa, sb, color, width, antialiased: true);
    }

    /// <summary>SketchUp's inference marker at the snapped point, and its tooltip.</summary>
    protected void DrawInference(Control overlay)
    {
        if (Current is not { } inf || View.ToScreen(inf.Point) is not { } p)
            return;

        if (inf is { Kind: InferenceKind.OnAxis, AxisFrom: { } from, AxisDirection: { } dir })
            DrawWorldLine(overlay, from, inf.Point, AxisColor(dir), 1, dashed: true);

        var (fill, shape) = inf.Kind switch
        {
            InferenceKind.Endpoint => (new Color(0, 0.75f, 0), "circle"),
            InferenceKind.Midpoint => (new Color(0, 0.75f, 0.85f), "circle"),
            InferenceKind.Origin => (new Color(0.9f, 0.85f, 0), "circle"),
            InferenceKind.OnEdge => (new Color(0.9f, 0, 0), "square"),
            InferenceKind.OnFace => (new Color(0, 0, 0.9f), "diamond"),
            InferenceKind.OnAxis => (AxisColor(inf.AxisDirection), "circle"),
            InferenceKind.OnGuide => (new Color(0.25f, 0.25f, 0.25f), "square"),
            InferenceKind.GuidePoint => (new Color(0, 0.75f, 0), "circle"),
            _ => (Colors.Black, "dot"),
        };
        switch (shape)
        {
            case "circle":
                overlay.DrawCircle(p, 6, fill);
                overlay.DrawArc(p, 6, 0, Mathf.Tau, 24, Colors.Black, 1, true);
                break;
            case "square":
                overlay.DrawRect(new Rect2(p - new Vector2(5, 5), new Vector2(10, 10)), fill);
                break;
            case "diamond":
                overlay.DrawColoredPolygon([p + new Vector2(0, -6), p + new Vector2(6, 0), p + new Vector2(0, 6), p + new Vector2(-6, 0)], fill);
                break;
            default:
                overlay.DrawCircle(p, 2, fill);
                break;
        }

        if (inf.Label.Length > 0)
        {
            var font = overlay.GetThemeDefaultFont();
            var size = 13;
            var textSize = font.GetStringSize(inf.Label, HorizontalAlignment.Left, -1, size);
            var box = new Rect2(p + new Vector2(14, 10), textSize + new Vector2(8, 4));
            overlay.DrawRect(box, new Color(1, 1, 0.88f));
            overlay.DrawRect(box, new Color(0.3f, 0.3f, 0.3f), filled: false, width: 1);
            overlay.DrawString(font, box.Position + new Vector2(4, textSize.Y - 1), inf.Label, HorizontalAlignment.Left, -1, size, Colors.Black);
        }
    }
}
