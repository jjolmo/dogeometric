using Dogeometric.Core.Geometry;
using Dogeometric.Core.IO;
using Dogeometric.Core.Modeling;
using Dogeometric.Core.View;

namespace Dogeometric.Core.Tests;

public class PhotoMatchTests
{
    /// <summary>Where a camera with this eye, target and vertical field of view puts a point on its photo.</summary>
    private static Func<Vec3, PhotoPoint> Camera(Vec3 eye, Vec3 target, double fov)
    {
        var ahead = (target - eye).Normalized();
        var right = ahead.Cross(Vec3.UnitZ).Normalized();
        var up = right.Cross(ahead);
        var f = 0.5 / Math.Tan(fov * Math.PI / 360);
        return p =>
        {
            var d = p - eye;
            var z = d.Dot(ahead);
            return new PhotoPoint(d.Dot(right) / z * f, d.Dot(up) / z * f);
        };
    }

    [Theory]
    [InlineData(-3000, -4000, 1500, 35)]
    [InlineData(-1200, -500, 800, 60)]
    [InlineData(-200, -2500, 300, 25)]
    public void Lines_of_a_known_photo_give_back_its_camera(double x, double y, double z, double fov)
    {
        var eye = new Vec3(x, y, z);
        var target = new Vec3(200, 300, 100);
        var shoot = Camera(eye, target, fov);
        (PhotoPoint, PhotoPoint) Line(Vec3 a, Vec3 b) => (shoot(a), shoot(b));
        var photo = new MatchedPhoto
        {
            Width = 4, Height = 3,
            Red = [Line(new(0, 0, 0), new(600, 0, 0)), Line(new(0, 400, 300), new(600, 400, 300))],
            Green = [Line(new(0, 0, 0), new(0, 400, 0)), Line(new(600, 0, 300), new(600, 400, 300))],
            Origin = shoot(Vec3.Zero),
            Distance = eye.Length,
        };

        var cam = PhotoMatch.Solve(photo)!.Value;
        Assert.Equal(fov, cam.FovDegrees, 6);
        Assert.True(cam.Eye.DistanceTo(eye) < 1e-6, $"{cam.Eye} vs {eye}");
        Assert.True((cam.Target - cam.Eye).Normalized().Dot((target - eye).Normalized()) > 1 - 1e-9);
        Assert.True(cam.Up.Z > 0);
    }

    [Fact]
    public void Parallel_lines_or_a_bad_pair_give_no_camera()
    {
        var photo = new MatchedPhoto { Red = [(new(0, 0), new(1, 0)), (new(0, 1), new(1, 1))] };
        Assert.Null(PhotoMatch.Solve(photo));
        // Both vanishing points on the same side of the centre: no real focal length.
        photo = new MatchedPhoto
        {
            Red = [(new(0, 0), new(1, 0.1)), (new(0, -0.2), new(1, 0.05))],
            Green = [(new(0, 0), new(0.5, 0.1)), (new(0, -0.2), new(0.5, 0.0))],
        };
        Assert.Null(PhotoMatch.Solve(photo));
    }

    [Fact]
    public void A_scenes_matched_photo_survives_saving()
    {
        var model = new Model();
        var photo = new MatchedPhoto { Name = "front.jpg", Image = [1, 2, 3, 250], Width = 640, Height = 480, Origin = new(0.1, -0.2), Distance = 1234 };
        model.Scenes.Add(new Scene { Name = "front.jpg", Photo = photo });
        var path = Path.Combine(Path.GetTempPath(), $"photo-{Guid.NewGuid()}.dog");
        DogFile.Save(model, path);
        var back = DogFile.Load(path).Scenes[0].Photo!;
        File.Delete(path);
        Assert.Equal(photo.Image, back.Image);
        Assert.Equal((640, 480, "front.jpg", 1234.0), (back.Width, back.Height, back.Name, back.Distance));
        Assert.Equal(photo.Origin, back.Origin);
        Assert.Equal(photo.Red, back.Red);
        Assert.Equal(photo.Green, back.Green);
    }

    [Fact]
    public void A_photos_corners_frame_its_view()
    {
        var photo = new MatchedPhoto { Width = 4, Height = 3, Distance = 1000,
            Red = [(new(-0.4, -0.1), new(-0.1, 0.02)), (new(-0.4, -0.3), new(-0.1, -0.1))],
            Green = [(new(0.1, 0.02), new(0.4, -0.1)), (new(0.1, -0.1), new(0.4, -0.3))],
            Origin = new(0, -0.2) };
        var cam = PhotoMatch.Solve(photo)!.Value;
        var corners = PhotoMatch.Corners(photo, 500)!;
        var centre = corners.Aggregate(Vec3.Zero, (a, p) => a + p) / 4;
        Assert.True(centre.DistanceTo(cam.Eye + (cam.Target - cam.Eye).Normalized() * 500) < 1e-6);
        Assert.Equal(4.0 / 3, corners[0].DistanceTo(corners[1]) / corners[1].DistanceTo(corners[2]), 9);
    }
}
