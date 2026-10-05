using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.View;

/// <summary>A point on a photo: centred, y up, in photo heights (the photo spans y from -0.5 to 0.5).</summary>
public readonly record struct PhotoPoint(double X, double Y)
{
    public static PhotoPoint operator -(PhotoPoint a, PhotoPoint b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>Camera › Match New Photo: a photo with the red and green axis lines and the origin placed on it.</summary>
public sealed class MatchedPhoto
{
    public string Name { get; set; } = "";
    public byte[] Image { get; set; } = [];
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    /// <summary>Two lines along the red axis and two along the green one, as the photo shows them.</summary>
    public (PhotoPoint A, PhotoPoint B)[] Red { get; set; } = [(new(-0.45, -0.05), new(-0.1, 0.05)), (new(-0.45, -0.3), new(-0.1, -0.12))];
    public (PhotoPoint A, PhotoPoint B)[] Green { get; set; } = [(new(0.1, 0.05), new(0.45, -0.05)), (new(0.1, -0.12), new(0.45, -0.3))];
    public PhotoPoint Origin { get; set; } = new(0, -0.25);

    /// <summary>How far the model origin is from the camera, in mm: the photo's scale.</summary>
    public double Distance { get; set; } = 5000;

    public double Aspect => (double)Width / Math.Max(1, Height);

    public MatchedPhoto Clone() => (MatchedPhoto)MemberwiseClone();
}

/// <summary>A photo's camera from its two vanishing points: the axes point at them, and their being square to each other
/// gives the focal length (the principal point is the photo's centre).</summary>
public static class PhotoMatch
{
    /// <summary>The photo's corners in the model, <paramref name="distance"/> in front of its camera (bottom left,
    /// bottom right, top right, top left); null when its lines give no camera.</summary>
    public static Vec3[]? Corners(MatchedPhoto photo, double distance)
    {
        if (Solve(photo) is not { } cam)
            return null;
        var ahead = (cam.Target - cam.Eye).Normalized();
        var up = cam.Up.Normalized();
        var right = ahead.Cross(up).Normalized();
        var h = Math.Tan(cam.FovDegrees * Math.PI / 360) * distance;
        var w = h * photo.Aspect;
        var c = cam.Eye + ahead * distance;
        return [c - right * w - up * h, c + right * w - up * h, c + right * w + up * h, c - right * w + up * h];
    }

    public static PhotoPoint? VanishingPoint((PhotoPoint A, PhotoPoint B) l1, (PhotoPoint A, PhotoPoint B) l2)
    {
        var (d1, d2) = (l1.B - l1.A, l2.B - l2.A);
        var den = d1.X * d2.Y - d1.Y * d2.X;
        if (Math.Abs(den) < 1e-12)
            return null;
        var w = l2.A - l1.A;
        var t = (w.X * d2.Y - w.Y * d2.X) / den;
        return new PhotoPoint(l1.A.X + d1.X * t, l1.A.Y + d1.Y * t);
    }

    /// <summary>The matched camera, or null when the lines give no camera (parallel, or vanishing points on one side).</summary>
    public static CameraState? Solve(MatchedPhoto photo)
    {
        if (VanishingPoint(photo.Red[0], photo.Red[1]) is not { } vx || VanishingPoint(photo.Green[0], photo.Green[1]) is not { } vy)
            return null;
        var dot = vx.X * vy.X + vx.Y * vy.Y;
        if (dot >= 0)
            return null;
        var f = Math.Sqrt(-dot);
        // World axes seen from the camera (x right, y up, z ahead): a left-handed frame, so blue is y × x.
        var x = new Vec3(vx.X, vx.Y, f).Normalized();
        var y = new Vec3(vy.X, vy.Y, f).Normalized();
        y = (y - x * x.Dot(y)).Normalized();
        var z = y.Cross(x);
        if (z.Y < 0)
        {
            y = -y;
            z = -z;
        }
        // The camera's own axes in world space.
        var right = new Vec3(x.X, y.X, z.X);
        var up = new Vec3(x.Y, y.Y, z.Y);
        var ahead = new Vec3(x.Z, y.Z, z.Z);
        var toOrigin = (right * photo.Origin.X + up * photo.Origin.Y + ahead * f).Normalized();
        var eye = -toOrigin * photo.Distance;
        var fov = 2 * Math.Atan(0.5 / f) * 180 / Math.PI;
        return new CameraState(eye, eye + ahead * photo.Distance, up, true, fov, 5000);
    }
}
