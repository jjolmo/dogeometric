using Dogeometric.Core.Geometry;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class TwoPointPerspectiveTests
{
    [Fact]
    public void Two_point_perspective_levels_the_view_and_remembers_the_pitch_as_a_lens_shift()
    {
        var camera = new ViewCamera(new Vec3(-1000, -1000, 800), new Vec3(0, 0, 0));
        var pitch = Math.Atan2(camera.Direction.Z, Math.Sqrt(camera.Direction.X * camera.Direction.X + camera.Direction.Y * camera.Direction.Y));
        var eye = camera.Eye;
        Assert.True(camera.SetTwoPoint());
        Assert.Equal(0, camera.Direction.Z, 9);
        Assert.Equal(eye, camera.Eye);
        Assert.Equal(Vec3.UnitZ, camera.Up);
        Assert.Equal(Math.Tan(pitch), camera.TwoPointShift!.Value, 9);

        camera.Orbit(Vec3.Zero, 0.1, 0);
        Assert.Null(camera.TwoPointShift);
    }

    [Fact]
    public void Looking_straight_down_has_no_two_point_view()
    {
        var camera = new ViewCamera(new Vec3(0, 0, 1000), Vec3.Zero);
        Assert.False(camera.SetTwoPoint());
        Assert.Null(camera.TwoPointShift);
    }
}
