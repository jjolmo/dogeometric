using Dogeometric.Core.Geometry;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class ViewCameraTests
{
    private const int Precision = 6;

    private static void AssertClose(Vec3 expected, Vec3 actual, double tolerance = 1e-6)
    {
        Assert.True(expected.DistanceTo(actual) <= tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void Yaw_orbit_keeps_distance_and_height()
    {
        var cam = new ViewCamera(new Vec3(1000, 0, 500), Vec3.Zero);
        cam.Orbit(Vec3.Zero, Math.PI / 2, 0);

        AssertClose(new Vec3(0, 1000, 500), cam.Eye);
        Assert.Equal(Math.Sqrt(1000 * 1000 + 500 * 500), cam.Distance, Precision);
    }

    [Fact]
    public void Orbit_with_gravity_keeps_up_vector_vertical_plane()
    {
        var cam = new ViewCamera(new Vec3(-3000, -4000, 2000), new Vec3(0, 0, 500));
        cam.Orbit(new Vec3(100, 200, 0), 0.7, -0.3);

        // No roll: the camera's right vector stays horizontal.
        Assert.Equal(0, cam.Right.Z, Precision);
        Assert.True(cam.Up.Z > 0);
    }

    [Fact]
    public void Orbit_with_gravity_stops_before_flipping_over_the_top()
    {
        var cam = new ViewCamera(new Vec3(0, -1000, 0), Vec3.Zero);
        var before = cam.Save();
        // Pitching far past the zenith must be rejected rather than flipping the view.
        cam.Orbit(Vec3.Zero, 0, Math.PI * 0.9);

        Assert.Equal(before, cam.Save());
    }

    [Fact]
    public void Pan_moves_eye_and_target_together()
    {
        var cam = new ViewCamera(new Vec3(0, -1000, 0), Vec3.Zero);
        var offset = cam.Target - cam.Eye;
        cam.Pan(100, 0, 1000, 1000);

        AssertClose(offset, cam.Target - cam.Eye);
        // Dragging the mouse right moves the camera left (scene follows the cursor).
        Assert.True(cam.Eye.X < 0);
        Assert.Equal(0, cam.Eye.Z, Precision);
    }

    [Fact]
    public void Pan_keeps_point_at_depth_under_cursor()
    {
        var cam = new ViewCamera(new Vec3(0, -1000, 0), Vec3.Zero);
        var wpp = cam.WorldPerPixel(1000, 1000);
        cam.Pan(50, 0, 1000, 1000);

        Assert.Equal(-50 * wpp, cam.Eye.X, Precision);
    }

    [Fact]
    public void Perspective_zoom_moves_toward_anchor()
    {
        var cam = new ViewCamera(new Vec3(0, -1000, 0), Vec3.Zero);
        cam.ZoomAt(Vec3.Zero, 2);

        AssertClose(new Vec3(0, -500, 0), cam.Eye);
    }

    [Fact]
    public void Zooming_up_to_a_small_part_brings_the_orbit_target_along()
    {
        // From an architectural view 10 m away, the wheel goes in on a 1 mm detail beside the target.
        var cam = new ViewCamera(new Vec3(0, -10000, 0), Vec3.Zero);
        var detail = new Vec3(500, 0, 0);
        for (var i = 0; i < 40; i++)
            cam.ZoomAt(detail, 1.25);

        // The near plane follows the target's distance: it must shrink with the view, not stay at metres.
        Assert.True(cam.Distance < 10, $"target still {cam.Distance} mm away");
        Assert.True(cam.Eye.DistanceTo(detail) < 10);
        AssertClose(new Vec3(0, 1, 0), cam.Direction);
    }

    [Fact]
    public void Parallel_zoom_shrinks_view_and_keeps_anchor_fixed()
    {
        var cam = new ViewCamera(new Vec3(0, -1000, 0), Vec3.Zero) { Perspective = false, OrthoHeight = 1000 };
        var anchor = new Vec3(200, 0, 100);
        cam.ZoomAt(anchor, 2);

        Assert.Equal(500, cam.OrthoHeight, Precision);
        // Anchor's offset from the view axis halves in world units, so its screen position is unchanged.
        AssertClose(new Vec3(100, -1000, 50), cam.Eye);
    }

    [Fact]
    public void Zoom_extents_frames_bounds_in_view()
    {
        var cam = new ViewCamera(new Vec3(-1, -1, 1), Vec3.Zero);
        var bounds = new Bounds3(new Vec3(-100, -100, 0), new Vec3(100, 100, 200));
        cam.ZoomExtents(bounds, 16.0 / 9);

        AssertClose(bounds.Center, cam.Target);
        var radius = bounds.Diagonal / 2;
        Assert.Equal(radius / Math.Sin(cam.HalfFovRadians), cam.Distance, Precision);
    }

    [Theory]
    [InlineData(StandardView.Top, 0, 0, -1)]
    [InlineData(StandardView.Front, 0, 1, 0)]
    [InlineData(StandardView.Right, -1, 0, 0)]
    public void Standard_views_look_along_axes(StandardView view, double x, double y, double z)
    {
        var cam = new ViewCamera(new Vec3(-2000, -3000, 1000), Vec3.Zero);
        cam.SetStandardView(view);

        AssertClose(new Vec3(x, y, z), cam.Direction);
    }

    [Fact]
    public void Iso_picks_nearest_quadrant()
    {
        var cam = new ViewCamera(new Vec3(3000, -2000, 500), Vec3.Zero);
        cam.SetStandardView(StandardView.Iso);

        var d = cam.Direction;
        Assert.True(d.X < 0 && d.Y > 0 && d.Z < 0);
    }

    [Fact]
    public void History_goes_back_and_forward()
    {
        var history = new CameraHistory();
        var a = new CameraState(Vec3.UnitX, Vec3.Zero, Vec3.UnitZ, true, 35, 1);
        var b = a with { Eye = Vec3.UnitY };
        var c = a with { Eye = -Vec3.UnitX };

        history.Record(a);
        history.Record(b);
        Assert.Equal(b, history.Back(c));
        Assert.Equal(a, history.Back(b));
        Assert.Null(history.Back(a));
        Assert.Equal(b, history.Forward(a));
    }
}
