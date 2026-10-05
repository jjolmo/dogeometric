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
        Inference.Axes = doc.Model.Axes;
        Inference.Centers = View.CenterPoints();
        Current = Inference.Infer(new ViewProjection(View), Mouse.X, Mouse.Y, From, doc.Context.Entities, doc.Context.ToWorld);
        if (From is { } from && doc.Model.Options is { LengthSnapping: true, LengthSnap: var step })
            Current = InferenceEngine.SnapLength(Current, from, step);
        OnInferenceChanged();
        View.QueueOverlayRedraw();
    }

    protected virtual void OnInferenceChanged() { }

    public override bool KeyDown(InputEventKey key)
    {
        // Arrow keys toggle an axis lock (→ red, ← green, ↑ blue), like SketchUp.
        Vec3? axis = key.Keycode switch
        {
            Key.Right => Red,
            Key.Left => Green,
            Key.Up => Blue,
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

    /// <summary>Shape tools' plane lock (Rectangle, arcs): the drawing plane's normal, or null to follow inference.</summary>
    protected Vec3? LockedNormal { get; private set; }

    /// <summary>→ red, ← green, ↑ blue: lock the plane perpendicular to that axis (again to unlock); ↓ the plane of the
    /// face under the cursor. Returns whether the key was one of them.</summary>
    protected bool TogglePlaneLock(InputEventKey key)
    {
        Vec3? axis = key.Keycode switch { Key.Right => Red, Key.Left => Green, Key.Up => Blue, _ => null };
        if (axis is { } a)
            LockedNormal = LockedNormal is { } l && l.Dot(a) > 0.999 ? null : a;
        else if (key.Keycode == Key.Down)
            LockedNormal = LockedNormal == null && Current is { Face: { } f } c ? c.EntityToWorld.ApplyNormal(f.Normal).Normalized() : null;
        else
            return false;
        OnPlaneLockChanged();
        RefreshStatus();
        UpdateInference();
        return true;
    }

    protected virtual void OnPlaneLockChanged() { }

    /// <summary>Preview colour: the locked axis's, black when free.</summary>
    protected Color LockColor => LockedNormal is { } n ? AxisColor(n) : Colors.Black;

    protected string LockHint => LockedNormal is { } n
        ? IsAxis(n) ? $" Locked to the {(Math.Abs(n.Dot(Red)) > 0.99 ? "red" : Math.Abs(n.Dot(Green)) > 0.99 ? "green" : "blue")} axis." : " Locked to the plane."
        : "";

    protected void ResetLocks()
    {
        Inference.LockedAxis = null;
        Inference.LockedLine = null;
    }

    /// <summary>
    /// SketchUp's typed coordinates: "[x,y,z]" a point in the drawing axes, "&lt;x,y,z&gt;" an offset along them from
    /// <paramref name="from"/> (world). Each value is a length with optional units; missing ones are 0.
    /// </summary>
    protected bool TryCoordinates(string text, Vec3? from, out Vec3 point)
    {
        point = default;
        var t = text.Trim();
        var absolute = t.StartsWith('[') && t.EndsWith(']');
        if (!absolute && !(t.StartsWith('<') && t.EndsWith('>')) || !absolute && from == null)
            return false;
        var parts = t[1..^1].Split(',');
        if (parts.Length is < 1 or > 3)
            return false;
        var v = new double[3];
        for (var i = 0; i < parts.Length; i++)
            if (parts[i].Trim().Length > 0 && !UI.Measure.Read(parts[i], out v[i]))
                return false;
        var along = Red * v[0] + Green * v[1] + Blue * v[2];
        point = absolute ? Axes.Origin + along : from!.Value + along;
        return true;
    }

    private Vector2? _pressedAt;

    /// <summary>A drawing's first click: remembers where, so its release can finish it (Preferences › Drawing › Click Style).</summary>
    protected void Pressed(Vector2 position) => _pressedAt = position;

    /// <summary>
    /// Whether releasing the button at <paramref name="position"/> finishes what the first click began: always for
    /// click-drag-release, after a drag for auto detect, never for click-move-click.
    /// </summary>
    protected bool ReleaseFinishes(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _pressedAt is not { } pressed)
            return false;
        _pressedAt = null;
        var style = UI.AppPreferences.Current.ClickStyle;
        return style == 0 || style == 1 && position.DistanceTo(pressed) > 6;
    }

    /// <summary>World point → active context's local point (where new geometry goes).</summary>
    protected Vec3 ToLocal(Vec3 world) => View.Document!.Context.ToWorld.Inverse().ApplyPoint(world);

    // ------------------------------------------------------------------ feedback drawing

    // The model's drawing axes (Axes tool), as unit directions.
    protected Transform Axes => View.Document?.Model.Axes ?? Transform.Identity;
    protected Vec3 Red => Axes.X.Normalized();
    protected Vec3 Green => Axes.Y.Normalized();
    protected Vec3 Blue => Axes.Z.Normalized();

    /// <summary>The drawing-axes plane most facing the viewer (its normal).</summary>
    protected Vec3 MostFacingPlane() => InferenceEngine.MostFacing(View.Camera.Direction, Axes);

    protected bool IsAxis(Vec3 d) => Math.Abs(d.Dot(Red)) > 0.99 || Math.Abs(d.Dot(Green)) > 0.99 || Math.Abs(d.Dot(Blue)) > 0.99;

    protected Color AxisColor(Vec3? dir) => dir switch
    {
        { } d when Math.Abs(d.Dot(Red)) > 0.99 => UI.AppPreferences.Current.RedAxis,
        { } d when Math.Abs(d.Dot(Green)) > 0.99 => UI.AppPreferences.Current.GreenAxis,
        { } d when Math.Abs(d.Dot(Blue)) > 0.99 => UI.AppPreferences.Current.BlueAxis,
        { } => UI.AppPreferences.Current.Parallel,
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
        if (UI.AppPreferences.Current.DisplayCrosshairs)
        {
            // Preferences › Drawing › Display crosshairs: short axis-coloured lines through the point.
            var size = View.Camera.Distance * 0.06;
            foreach (var axis in new[] { Red, Green, Blue })
                DrawWorldLine(overlay, inf.Point - axis * size, inf.Point + axis * size, AxisColor(axis), 1);
        }

        if (inf is { Kind: InferenceKind.OnAxis, AxisFrom: { } from, AxisDirection: { } dir })
            DrawWorldLine(overlay, from, inf.Point, AxisColor(dir), 1, dashed: true);

        // SketchUp 2021's markers: a filled shape with a white rim; purple for points inside other groups.
        var group = new Color("#745aa6");
        var (fill, shape) = inf.Kind switch
        {
            InferenceKind.Endpoint => (new Color("#5e9440"), "circle"),
            InferenceKind.Midpoint => (new Color("#36c0c8"), "circle"),
            InferenceKind.Origin => (new Color("#363545"), "origin"),
            InferenceKind.OnEdge => (new Color("#bb2025"), "square"),
            InferenceKind.OnFace => (new Color("#005f9f"), "diamond"),
            InferenceKind.OnAxis => (AxisColor(inf.AxisDirection), "circle"),
            InferenceKind.OnGuide => (new Color("#bb2025"), "square"),
            InferenceKind.GuidePoint => (new Color("#5e9440"), "circle"),
            InferenceKind.Center => (CenterMarker.Color, "center"),
            _ => (Colors.Black, "dot"),
        };
        if (inf.InGroup)
            fill = group;
        var rim = new Color("#f8f9fc");
        const float r = 5.5f, edge = 1.5f;
        switch (shape)
        {
            case "circle":
            case "origin":
                overlay.DrawCircle(p, r + edge, rim);
                overlay.DrawCircle(p, r, fill);
                if (shape == "origin")
                {
                    overlay.DrawArc(p, r * 0.55f, 0, Mathf.Tau, 16, rim, 1.2f, true);
                    overlay.DrawLine(p - new Vector2(r * 0.8f, 0), p + new Vector2(r * 0.8f, 0), rim, 1.2f);
                    overlay.DrawLine(p - new Vector2(0, r * 0.8f), p + new Vector2(0, r * 0.8f), rim, 1.2f);
                }
                break;
            case "square":
            {
                var h = r * 0.88f;
                overlay.DrawRect(new Rect2(p - new Vector2(h + edge, h + edge), new Vector2(2 * (h + edge), 2 * (h + edge))), rim);
                overlay.DrawRect(new Rect2(p - new Vector2(h, h), new Vector2(2 * h, 2 * h)), fill);
                break;
            }
            case "diamond":
            {
                var h = r * 1.25f;
                var o = h + edge * 1.41f;
                overlay.DrawColoredPolygon([p + new Vector2(0, -o), p + new Vector2(o, 0), p + new Vector2(0, o), p + new Vector2(-o, 0)], rim);
                overlay.DrawColoredPolygon([p + new Vector2(0, -h), p + new Vector2(h, 0), p + new Vector2(0, h), p + new Vector2(-h, 0)], fill);
                break;
            }
            case "center":
                CenterMarker.Draw(overlay, p, fill, 6.5f);
                break;
            default:
                overlay.DrawCircle(p, 2, fill);
                break;
        }

        if (inf.Label.Length > 0)
            DrawTooltip(overlay, p, inf.Label);
    }

    /// <summary>SketchUp's yellow inference tooltip beside screen point <paramref name="p"/>.</summary>
    protected static void DrawTooltip(Control overlay, Vector2 p, string text)
    {
        var font = overlay.GetThemeDefaultFont();
        var size = 13;
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, size);
        var box = new Rect2(p + new Vector2(14, 10), textSize + new Vector2(8, 4));
        overlay.DrawRect(box, new Color(1, 1, 0.88f));
        overlay.DrawRect(box, new Color(0.3f, 0.3f, 0.3f), filled: false, width: 1);
        overlay.DrawString(font, box.Position + new Vector2(4, textSize.Y - 1), text, HorizontalAlignment.Left, -1, size, Colors.Black);
    }
}
