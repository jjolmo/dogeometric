using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.View;

public enum StandardView
{
    Top,
    Bottom,
    Front,
    Back,
    Left,
    Right,
    Iso,
}

/// <summary>
/// The model camera, in model space (Z-up, mm). Implements SketchUp's navigation maths and knows nothing about
/// the renderer: callers pass viewport sizes in pixels.
/// </summary>
public sealed class ViewCamera
{
    /// <summary>SketchUp's default field of view, in degrees, measured vertically.</summary>
    public const double DefaultFov = 35;

    // Orbit stops this close to straight up/down while gravity is on, so vertical edges never flip.
    private const double MinPolarAngle = 0.5 * Math.PI / 180;

    public Vec3 Eye { get; private set; }
    public Vec3 Target { get; private set; }
    public Vec3 Up { get; private set; } = Vec3.UnitZ;
    public bool Perspective { get; set; } = true;
    public double FovDegrees { get; set; } = DefaultFov;

    /// <summary>Visible height of the view in parallel projection, in mm.</summary>
    public double OrthoHeight { get; set; } = 5000;

    public Vec3 Direction => (Target - Eye).Normalized();
    public Vec3 Right => Direction.Cross(Up).Normalized();
    public double Distance => Eye.DistanceTo(Target);
    public double HalfFovRadians => FovDegrees * Math.PI / 360;

    public ViewCamera(Vec3 eye, Vec3 target)
    {
        Set(eye, target, Vec3.UnitZ);
    }

    public void Set(Vec3 eye, Vec3 target, Vec3 up)
    {
        Eye = eye;
        Target = target;
        Up = OrthogonalUp(Direction, up);
    }

    public CameraState Save() => new(Eye, Target, Up, Perspective, FovDegrees, OrthoHeight);

    public void Restore(CameraState s)
    {
        Eye = s.Eye;
        Target = s.Target;
        Up = s.Up;
        Perspective = s.Perspective;
        FovDegrees = s.FovDegrees;
        OrthoHeight = s.OrthoHeight;
    }

    /// <summary>
    /// Orbits around <paramref name="pivot"/>. Yaw turns about the world blue axis; pitch turns about the camera's
    /// right axis. With <paramref name="gravity"/> (SketchUp's default) the camera keeps vertical edges vertical and
    /// never rolls; holding Ctrl in SketchUp suspends gravity.
    /// </summary>
    public void Orbit(Vec3 pivot, double yaw, double pitch, bool gravity = true)
    {
        var yawAxis = gravity ? Vec3.UnitZ : Up;
        Rotate(pivot, yawAxis, yaw);

        var right = Right;
        var oldEye = Eye;
        var oldTarget = Target;
        var oldUp = Up;
        Rotate(pivot, right, pitch);

        if (!gravity)
            return;

        var polar = Direction.AngleTo(Vec3.UnitZ);
        var upsideDown = Up.Dot(Vec3.UnitZ) < 0;
        if (polar < MinPolarAngle || polar > Math.PI - MinPolarAngle || upsideDown)
        {
            Eye = oldEye;
            Target = oldTarget;
            Up = oldUp;
            return;
        }

        Up = OrthogonalUp(Direction, Vec3.UnitZ, fallback: Up);
    }

    /// <summary>
    /// Moves the camera parallel to the view plane so that a point at <paramref name="depth"/> follows the cursor.
    /// </summary>
    public void Pan(double dxPixels, double dyPixels, double viewportHeightPixels, double depth)
    {
        var worldPerPixel = WorldPerPixel(viewportHeightPixels, depth);
        var delta = (-Right * dxPixels + Up * dyPixels) * worldPerPixel;
        Eye += delta;
        Target += delta;
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> (&gt; 1 zooms in) keeping <paramref name="anchor"/> fixed on screen,
    /// as SketchUp's mouse wheel does with the point under the cursor.
    /// </summary>
    public void ZoomAt(Vec3 anchor, double factor)
    {
        if (factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor));

        var dir = Direction;
        if (Perspective)
        {
            var delta = (anchor - Eye) * (1 - 1 / factor);
            Eye += delta;
            Target += delta;
            // Keep the orbit target in front of the camera after zooming past it.
            if ((Target - Eye).Dot(dir) <= 0)
                Target = Eye + dir * Math.Max(anchor.DistanceTo(Eye), 1);
            return;
        }

        // Parallel projection: shrink the view and slide sideways so the anchor stays put.
        var toAnchor = anchor - Eye;
        var lateral = toAnchor - dir * toAnchor.Dot(dir);
        var shift = lateral * (1 - 1 / factor);
        Eye += shift;
        Target += shift;
        OrthoHeight /= factor;
    }

    /// <summary>Changes the field of view (Zoom tool with Shift, or Field of View tool).</summary>
    public void SetFov(double degrees) => FovDegrees = Math.Clamp(degrees, 1, 120);

    /// <summary>Frames <paramref name="bounds"/> keeping the current view direction.</summary>
    public void ZoomExtents(Bounds3 bounds, double aspect)
    {
        if (bounds.IsEmpty)
            return;

        var dir = Direction;
        var center = bounds.Center;
        var radius = Math.Max(bounds.Diagonal * 0.5, 1);
        var limitingHalfFov = aspect >= 1 ? HalfFovRadians : Math.Atan(Math.Tan(HalfFovRadians) * aspect);

        var distance = radius / Math.Sin(limitingHalfFov);
        Target = center;
        Eye = center - dir * distance;
        OrthoHeight = 2 * radius * Math.Max(1, 1 / aspect);
    }

    /// <summary>
    /// Points the camera along a standard view keeping the target and distance. Iso picks the isometric view
    /// nearest to the current one, as SketchUp does.
    /// </summary>
    public void SetStandardView(StandardView view)
    {
        var (dir, up) = view switch
        {
            StandardView.Top => (-Vec3.UnitZ, Vec3.UnitY),
            StandardView.Bottom => (Vec3.UnitZ, -Vec3.UnitY),
            StandardView.Front => (Vec3.UnitY, Vec3.UnitZ),
            StandardView.Back => (-Vec3.UnitY, Vec3.UnitZ),
            StandardView.Left => (Vec3.UnitX, Vec3.UnitZ),
            StandardView.Right => (-Vec3.UnitX, Vec3.UnitZ),
            StandardView.Iso => (NearestIsoDirection(Direction), Vec3.UnitZ),
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };

        var distance = Math.Max(Distance, 1);
        Eye = Target - dir * distance;
        Up = OrthogonalUp(dir, up);
    }

    /// <summary>Model-space size of one pixel at <paramref name="depth"/> along the view direction.</summary>
    public double WorldPerPixel(double viewportHeightPixels, double depth) => Perspective
        ? 2 * Math.Max(depth, 1e-6) * Math.Tan(HalfFovRadians) / viewportHeightPixels
        : OrthoHeight / viewportHeightPixels;

    /// <summary>Distance from the eye to <paramref name="point"/> measured along the view direction.</summary>
    public double DepthOf(Vec3 point) => (point - Eye).Dot(Direction);

    private void Rotate(Vec3 pivot, Vec3 axis, double angle)
    {
        if (angle == 0)
            return;
        Eye = Eye.RotatedAround(pivot, axis, angle);
        Target = Target.RotatedAround(pivot, axis, angle);
        Up = Up.RotatedAround(axis, angle).Normalized();
    }

    private static Vec3 NearestIsoDirection(Vec3 current)
    {
        var sx = current.X >= 0 ? 1 : -1;
        var sy = current.Y >= 0 ? 1 : -1;
        return new Vec3(sx, sy, -1).Normalized();
    }

    /// <summary>Component of <paramref name="up"/> perpendicular to <paramref name="dir"/>.</summary>
    private static Vec3 OrthogonalUp(Vec3 dir, Vec3 up, Vec3? fallback = null)
    {
        var ortho = up - dir * up.Dot(dir);
        if (!ortho.IsZero(1e-9))
            return ortho.Normalized();
        var alt = fallback ?? Vec3.UnitY;
        ortho = alt - dir * alt.Dot(dir);
        return ortho.IsZero(1e-9) ? Vec3.UnitX : ortho.Normalized();
    }
}

public readonly record struct CameraState(Vec3 Eye, Vec3 Target, Vec3 Up, bool Perspective, double FovDegrees, double OrthoHeight);
