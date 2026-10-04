using Godot;

namespace Dogeometric.App.Tools;

public sealed class OrbitTool : Tool
{
    private bool _dragging;

    public override int CommandId => CommandIds.Orbit;
    public override string CursorImage => "orbit";
    public override bool IsNavigation => true;
    public override string StatusText => "Drag to orbit. Shift = Pan, Ctrl = suspend gravity.";
    public override Input.CursorShape Cursor => Input.CursorShape.Move;

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _dragging = true;
        View.BeginOrbit(position);
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (!_dragging)
            return;
        // Shift turns Orbit into Pan while held, as in SketchUp.
        if (Input.IsKeyPressed(Key.Shift))
            View.PanBy(relative);
        else
            View.OrbitBy(relative, gravity: !Input.IsKeyPressed(Key.Ctrl));
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _dragging = false;
    }
}
