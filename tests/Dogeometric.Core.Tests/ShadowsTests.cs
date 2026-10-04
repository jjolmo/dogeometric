using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;

namespace Dogeometric.Core.Tests;

public class ShadowsTests
{
    private static (double Altitude, double Azimuth) Angles(ShadowSettings s)
    {
        var v = s.SunDirection();
        return (Math.Asin(v.Z) * 180 / Math.PI, (Math.Atan2(v.X, v.Y) * 180 / Math.PI + 360) % 360);
    }

    [Fact]
    public void The_midsummer_noon_sun_is_due_south_at_ninety_minus_latitude_plus_tilt()
    {
        var (alt, az) = Angles(new ShadowSettings { Time = new DateTime(2021, 6, 21, 12, 0, 0) });
        Assert.InRange(alt, 73.0, 73.8);
        Assert.InRange(az, 175, 185);
    }

    [Fact]
    public void The_morning_sun_rises_in_the_east_and_the_night_sun_is_below_the_ground()
    {
        var (alt, az) = Angles(new ShadowSettings { Time = new DateTime(2021, 6, 21, 7, 0, 0) });
        Assert.InRange(alt, 15, 30);
        Assert.InRange(az, 60, 90);
        Assert.True(new ShadowSettings { Time = new DateTime(2021, 6, 21, 23, 0, 0) }.SunDirection().Z < 0);
    }

    [Fact]
    public void The_north_angle_turns_the_sun_about_the_blue_axis()
    {
        var s = new ShadowSettings();
        var turned = (s with { NorthAngle = 90 }).SunDirection();
        var plain = s.SunDirection();
        Assert.Equal(plain.Y, turned.X, 9);
        Assert.Equal(-plain.X, turned.Y, 9);
        Assert.Equal(plain.Z, turned.Z, 9);
    }

    [Fact]
    public void Shadow_settings_survive_a_dog_round_trip()
    {
        var m = new Model { Shadows = new ShadowSettings { Enabled = true, Time = new DateTime(2021, 3, 2, 9, 15, 0), UtcOffset = 1, Light = 60, Dark = 20, OnGround = false, FromEdges = true } };
        var path = Path.Combine(Path.GetTempPath(), $"shadows-{Guid.NewGuid():N}.dog");
        try
        {
            DogFile.Save(m, path);
            Assert.Equal(m.Shadows, DogFile.Load(path).Shadows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Undo_puts_the_shadow_settings_back()
    {
        var doc = new Document(new Model());
        doc.Undo.Begin("Shadow Settings");
        doc.Model.Shadows = doc.Model.Shadows with { Enabled = true };
        doc.Undo.Commit();
        doc.Undo.Undo();
        Assert.False(doc.Model.Shadows.Enabled);
    }
}
