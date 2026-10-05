using Dogeometric.Core.Geometry;
using Dogeometric.Core.Inference;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.Picking;
using Dogeometric.Core.Units;
using Godot;

namespace Dogeometric.App.Tools;

/// <summary>
/// SketchUp's Rectangle tool: two opposite corners on the face under the first click (or the ground / axis plane
/// facing the viewer). Ctrl draws from the centre; typing "w,h" sets the size.
/// </summary>
public sealed class RectangleTool : DrawingTool
{
    private Vec3? _corner;
    private Vec3 _normal = Vec3.UnitZ;
    private Vec3 _uAxis = Vec3.UnitX;
    private Vec3 _vAxis = Vec3.UnitY;

    public override int CommandId => CommandIds.Rectangle;
    public override string CursorImage => "rectangle";
    public override string VcbLabel => "Dimensions";
    public override string StatusText => (_corner == null ? "Click to set first corner." : "Click to set opposite corner or enter length, width.") + LockHint;

    public override string VcbValue => Corners() is { } c
        ? $"{UI.Measure.Show(c[0].DistanceTo(c[1]))}, {UI.Measure.Show(c[1].DistanceTo(c[2]))}"
        : "";

    protected override void OnInferenceChanged() => View.ShowVcbValue(VcbValue);

    public override void MouseDown(MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left || Current is not { } inf)
            return;
        if (_corner == null)
        {
            _corner = inf.Point;
            ChoosePlane(inf);
            Pressed(position);
            RefreshStatus();
            return;
        }
        Create(Corners());
    }

    public override void MouseUp(MouseButton button, Vector2 position)
    {
        if (ReleaseFinishes(button, position) && _corner != null)
            Create(Corners());
    }

    /// <summary>The plane of the face clicked first, otherwise the axis plane most facing the viewer.</summary>
    private void ChoosePlane(InferenceResult inf)
    {
        if (LockedNormal is { } locked)
            _normal = locked;
        else if (inf.Face is { } f)
            _normal = inf.EntityToWorld.ApplyNormal(f.Normal);
        else if (inf.Kind == InferenceKind.InPlane)
            _normal = Blue; // on the ground
        else
            _normal = MostFacingPlane();
        SetAxes();
    }

    protected override void OnPlaneLockChanged()
    {
        if (_corner != null && Current is { } inf)
            ChoosePlane(inf);
    }

    private void SetAxes()
    {
        // In-plane axes follow the drawing axes when the plane is axis-aligned.
        var n = _normal;
        var candidates = new[] { Red, Green, Blue }.Where(a => Math.Abs(a.Dot(n)) < 0.99).ToList();
        _uAxis = (candidates[0] - n * candidates[0].Dot(n)).Normalized();
        _vAxis = n.Cross(_uAxis).Normalized();
    }

    /// <summary>Four corners on the drawing plane, or null before the first click.</summary>
    private Vec3[]? Corners(double? width = null, double? height = null)
    {
        if (_corner is not { } a || Current is not { } c)
            return null;
        var b = ProjectToPlane(c.Point, a);
        var du = (b - a).Dot(_uAxis);
        var dv = (b - a).Dot(_vAxis);
        if (width is { } w)
            du = Math.Sign(du == 0 ? 1 : du) * w;
        if (height is { } h)
            dv = Math.Sign(dv == 0 ? 1 : dv) * h;
        var origin = a;
        if (Input.IsKeyPressed(Key.Ctrl))
        {
            // From centre: the first click is the middle.
            origin = a - _uAxis * du - _vAxis * dv;
            du *= 2;
            dv *= 2;
        }
        return [origin, origin + _uAxis * du, origin + _uAxis * du + _vAxis * dv, origin + _vAxis * dv];
    }

    private Vec3 ProjectToPlane(Vec3 p, Vec3 onPlane)
    {
        // Prefer the cursor ray's intersection with the plane; fall back to projecting the inferred point.
        var ray = View.ScreenRay(Mouse);
        if ((LockedNormal != null || Current?.Kind is InferenceKind.InPlane or InferenceKind.None or InferenceKind.OnFace) &&
            InferenceEngine.IntersectPlane(new Ray(ray.Origin, ray.Direction), _normal, onPlane) is { } hit)
            return hit;
        return p - _normal * (p - onPlane).Dot(_normal);
    }

    public override bool ApplyVcb(string text)
    {
        var parts = text.Split(',', ';');
        if (parts.Length != 2 || !UI.Measure.Read(parts[0], out var w) || !UI.Measure.Read(parts[1], out var h))
            return false;
        return Create(Corners(Math.Abs(w), Math.Abs(h)));
    }

    private bool Create(Vec3[]? corners)
    {
        if (corners == null || View.Document is not { } doc)
            return false;
        if (corners[0].DistanceTo(corners[1]) <= Tolerance.Length || corners[1].DistanceTo(corners[2]) <= Tolerance.Length)
            return false;
        var local = corners.Select(ToLocal).ToList();
        var normal = View.Document.Context.ToWorld.Inverse().ApplyNormal(_normal);
        doc.Operation("Rectangle", e => StickyGeometry.DrawEdges(e, local, closed: true, normal));
        _corner = null;
        RefreshStatus();
        UpdateInference();
        return true;
    }

    public override bool KeyDown(InputEventKey key)
    {
        if (key.Keycode == Key.Escape && _corner != null)
        {
            _corner = null;
            RefreshStatus();
            View.QueueOverlayRedraw();
            return true;
        }
        return TogglePlaneLock(key) || base.KeyDown(key);
    }

    public override void Draw(Control overlay)
    {
        if (Corners() is { } c)
        {
            for (var i = 0; i < 4; i++)
                DrawWorldLine(overlay, c[i], c[(i + 1) % 4], LockColor, 1.5f);
        }
        DrawInference(overlay);
    }
}
