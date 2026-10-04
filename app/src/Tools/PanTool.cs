using Godot;

namespace Dogeometric.App.Tools;

public sealed class PanTool : Tool
{
    private bool _dragging;

    public override int CommandId => CommandIds.Pan;
    public override string CursorImage => "pan";
    public override bool IsNavigation => true;
    public override string StatusText => "Drag in direction to pan";
    public override Input.CursorShape Cursor => Input.CursorShape.Drag;

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _dragging = true;
        View.BeginPan(position);
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_dragging)
            View.PanBy(relative);
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _dragging = false;
    }
}
