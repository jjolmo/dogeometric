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
    public override string CursorImage => "zoom";
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

    public override string VcbValue => View.Camera.FovDegrees.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " deg";

    public override void Activate() => View.ShowVcbValue(VcbValue);

    /// <summary>Field of view in degrees ("35", "35deg"), or a lens focal length ("50mm", 35 mm film).</summary>
    public override bool ApplyVcb(string text)
    {
        var t = text.Trim().ToLowerInvariant().Replace(',', '.');
        double degrees;
        if (t.EndsWith("mm"))
        {
            if (!double.TryParse(t[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var focal) || focal <= 0)
                return false;
            degrees = 2 * Math.Atan(24.0 / 2 / focal) * 180 / Math.PI; // 24 mm frame height
        }
        else if (!double.TryParse(t.Replace("deg", "").Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out degrees))
        {
            return false;
        }
        View.BeginNavigation();
        View.ChangeFovBy(degrees - View.Camera.FovDegrees);
        View.ShowVcbValue(VcbValue);
        return true;
    }
}
