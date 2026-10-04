using Dogeometric.Core.Geometry;
using Godot;

namespace Dogeometric.App.Tools;

public sealed class ZoomTool : Tool
{
    // Fraction of the distance travelled per pixel dragged.
    private const double ZoomPerPixel = 0.01;
    private const double FovDegreesPerPixel = 0.25;

    private bool _dragging;
    private Vec3 _anchor;

    public override int CommandId => CommandIds.Zoom;
    public override bool IsNavigation => true;
    public override string StatusText => "Drag cursor to zoom.  Up is in, down is out. Shift to change Field of View.";
    public override string VcbLabel => "Field of View";
    public override Input.CursorShape Cursor => Input.CursorShape.Vsize;

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _dragging = true;
        View.BeginNavigation();
        _anchor = View.PickPoint(position);
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (!_dragging || relative.Y == 0)
            return;
        if (Input.IsKeyPressed(Key.Shift))
            View.ChangeFovBy(relative.Y * FovDegreesPerPixel);
        else
            View.ZoomAt(_anchor, Math.Exp(-relative.Y * ZoomPerPixel));
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _dragging = false;
    }
}
