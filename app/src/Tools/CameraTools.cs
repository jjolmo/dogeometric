using Dogeometric.Core.Geometry;
using Dogeometric.Core.Units;
using Dogeometric.Core.View;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>SketchUp's Look Around: drag to turn the view about the eye. The Measurements box sets the eye height.</summary>
public sealed class LookAroundTool : Tool
{
    private const double RadiansPerPixel = 0.0035;
    private bool _dragging;

    public override int CommandId => CommandIds.LookAround;
    public override string CursorImage => "positioncamera";
    public override bool IsNavigation => true;
    public override string StatusText => "Drag in direction to turn camera";
    public override string VcbLabel => "Eye Height";
    public override string VcbValue => UI.Measure.Show(View.Camera.Eye.Z);

    public override void Activate() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _dragging = true;
        View.BeginNavigation();
    }

    public override void MouseMove(Vector2 position, Vector2 relative)
    {
        if (_dragging)
            View.ChangeCamera(c => c.LookAround(-relative.X * RadiansPerPixel, -relative.Y * RadiansPerPixel));
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _dragging = false;
    }

    public override bool ApplyVcb(string text)
    {
        if (!UI.Measure.Read(text, out var height))
            return false;
        View.BeginNavigation();
        View.ChangeCamera(c => c.PlaceEye(new Vec3(c.Eye.X, c.Eye.Y, height), c.Direction));
        View.ShowVcbValue(VcbValue);
        return true;
    }
}

/// <summary>
/// SketchUp's Position Camera: click a point to stand there at eye height looking the same way, or press, drag
/// towards where to look and release. Then Look Around takes over, as in SketchUp.
/// </summary>
public sealed class PositionCameraTool : Tool
{
    private Vec3? _down;
    private Vector2 _downScreen;
    private double _height = ViewCamera.DefaultEyeHeight;

    public override int CommandId => CommandIds.PositionCamera;
    public override string CursorImage => "positioncamera";
    public override string StatusText => "Click to stand at eye height, or drag to also set the view direction.";
    public override string VcbLabel => "Height Offset";
    public override string VcbValue => UI.Measure.Show(_height);

    public override void Activate() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _down = View.PickGround(position);
        _downScreen = position;
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || _down is not { } p)
            return;
        _down = null;
        View.BeginNavigation();
        if (position.DistanceTo(_downScreen) > 4)
        {
            // Dragged: stand on the first point at eye height, look at the second.
            var target = View.PickGround(position) ?? View.PickPoint(position);
            var eye = p + Vec3.UnitZ * _height;
            View.ChangeCamera(c => c.PlaceEye(eye, target + Vec3.UnitZ * _height - eye));
        }
        else
        {
            View.ChangeCamera(c => c.PlaceEye(p + Vec3.UnitZ * _height, new Vec3(c.Direction.X, c.Direction.Y, 0)));
        }
        Manager.Activate(new LookAroundTool());
    }

    public override bool ApplyVcb(string text)
    {
        if (!UI.Measure.Read(text, out var h))
            return false;
        _height = h;
        View.ShowVcbValue(VcbValue);
        return true;
    }
}

/// <summary>
/// SketchUp's Walk: press and drag; up/down walks forward/back, left/right turns, the further the faster. Shift
/// moves up/down and sideways instead, Ctrl runs.
/// </summary>
public sealed class WalkTool : Tool
{
    private const double MmPerPixelPerSecond = 6;
    private const double RadiansPerPixelPerSecond = 0.004;

    private Vector2? _anchor;
    private Vector2 _mouse;

    public override int CommandId => CommandIds.Walk;
    public override string CursorImage => "walk";
    public override bool IsNavigation => true;
    public override string StatusText => "Click and drag to walk.  Ctrl = run, Shift = move vertically or sideways, Alt = disable collision detection";
    public override string VcbLabel => "Eye Height";
    public override string VcbValue => UI.Measure.Show(View.Camera.Eye.Z);

    public override void Activate() => View.ShowVcbValue(VcbValue);

    /// <summary>A typed eye height moves the eye up or down, looking the same way, as in Look Around.</summary>
    public override bool ApplyVcb(string text)
    {
        if (!UI.Measure.Read(text, out var height))
            return false;
        View.BeginNavigation();
        View.ChangeCamera(c => c.PlaceEye(new Vec3(c.Eye.X, c.Eye.Y, height), c.Direction));
        View.ShowVcbValue(VcbValue);
        return true;
    }

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
            return;
        _anchor = _mouse = position;
        View.BeginNavigation();
        Step();
    }

    public override void MouseMove(Vector2 position, Vector2 relative) => _mouse = position;

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left)
            _anchor = null;
    }

    /// <summary>Moves a frame's worth while the button is held, then asks for the next frame.</summary>
    private void Step()
    {
        if (_anchor is not { } a || !GodotObject.IsInstanceValid(View))
            return;
        var dt = 1.0 / 60;
        var d = _mouse - a;
        var speed = MmPerPixelPerSecond * (Input.IsKeyPressed(Key.Ctrl) ? 3 : 1) * dt;
        if (Input.IsKeyPressed(Key.Shift))
            View.ChangeCamera(c => c.Walk(0, d.X * speed, -d.Y * speed, 0));
        else
            View.ChangeCamera(c => c.Walk(-d.Y * speed, 0, 0, -d.X * RadiansPerPixelPerSecond * dt));
        View.GetTree().CreateTimer(dt).Timeout += Step;
    }
}
