using Dogeometric.Core.Geometry;
using Dogeometric.Core.Modeling;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// Edit › Paste as SketchUp does it: the clipboard content follows the cursor (held by its lower front left
/// corner, snapping like the Move tool) and a click places it. Esc cancels.
/// </summary>
public sealed class PasteTool : DrawingTool
{
    public override int CommandId => CommandIds.Paste;
    public override string CursorImage => "move";
    public override string StatusText => "Pick insertion point.";

    private static Vec3 Grip => Clipboard.Bounds.Min;

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf || View.Document is not { } doc)
            return;
        Clipboard.Paste(doc, inf.Point - Grip);
        Manager.Activate(new SelectTool());
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape)
        {
            Manager.Activate(new SelectTool());
            return true;
        }
        return base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (Current is { } inf)
        {
            var offset = inf.Point - Grip;
            foreach (var (a, b) in Clipboard.PreviewLines())
                DrawWorldLine(overlay, a + offset, b + offset, new Color(0, 0, 1), 1);
        }
        DrawInference(overlay);
    }
}
