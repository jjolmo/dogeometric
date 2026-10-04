using Godot;

namespace Dogeometric.App.Tools;

/// <summary>SketchUp's Zoom Window: drag a rectangle around what should fill the view.</summary>
public sealed class ZoomWindowTool : Tool
{
    private Vector2? _down;
    private Vector2 _current;

    public override int CommandId => CommandIds.ZoomWindow;
    public override string CursorImage => "zoomwindow";
    public override bool IsNavigation => true;
    public override string StatusText => "Drag window area to zoom to";

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _down = _current = position;
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        _current = position;
        if (_down != null)
            View.QueueOverlayRedraw();
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _down is not { } down)
            return;
        _down = null;
        View.ZoomWindow(new Rect2(down, position - down).Abs());
        View.QueueOverlayRedraw();
    }

    public override void Draw(Control overlay)
    {
        if (_down is { } down)
            overlay.DrawRect(new Rect2(down, _current - down).Abs(), Colors.Black, filled: false, width: 1);
    }
}
