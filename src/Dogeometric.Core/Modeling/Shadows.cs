using Dogeometric.Core.Geometry;

namespace Dogeometric.Core.Modeling;

/// <summary>SketchUp's Shadows settings (Window › Shadows), saved with the model. Defaults are SketchUp's template's.</summary>
public sealed record ShadowSettings
{
    public bool Enabled { get; init; }

    /// <summary>Local time and date at the model's location; the year does not matter.</summary>
    public DateTime Time { get; init; } = new(2021, 11, 8, 13, 30, 0);

    /// <summary>Hours from UTC of <see cref="Time"/>.</summary>
    public double UtcOffset { get; init; } = -7;

    public double Latitude { get; init; } = 40.017;
    public double Longitude { get; init; } = -105.283;

    /// <summary>Degrees from the green axis to north, clockwise seen from above.</summary>
    public double NorthAngle { get; init; }

    /// <summary>Sunlight and ambient strength, 0 to 100.</summary>
    public int Light { get; init; } = 80;
    public int Dark { get; init; } = 45;

    /// <summary>Faces are shaded by the sun's direction even with shadows off.</summary>
    public bool UseSunForShading { get; init; }

    public bool OnFaces { get; init; } = true;
    public bool OnGround { get; init; } = true;
    public bool FromEdges { get; init; }

    /// <summary>Unit vector from the model towards the sun (red east, green north at a zero north angle, blue up).</summary>
    public Vec3 SunDirection() => Sun.Direction(Time, UtcOffset, Latitude, Longitude, NorthAngle);
}

/// <summary>The sun's position (NOAA's approximation, good to a fraction of a degree).</summary>
public static class Sun
{
    public static Vec3 Direction(DateTime local, double utcOffset, double latitude, double longitude, double northAngle)
    {
        var hours = local.TimeOfDay.TotalHours;
        var gamma = 2 * Math.PI / 365 * (local.DayOfYear - 1 + (hours - utcOffset - 12) / 24);
        var eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(gamma) - 0.032077 * Math.Sin(gamma)
            - 0.014615 * Math.Cos(2 * gamma) - 0.040849 * Math.Sin(2 * gamma));
        var decl = 0.006918 - 0.399912 * Math.Cos(gamma) + 0.070257 * Math.Sin(gamma) - 0.006758 * Math.Cos(2 * gamma)
            + 0.000907 * Math.Sin(2 * gamma) - 0.002697 * Math.Cos(3 * gamma) + 0.00148 * Math.Sin(3 * gamma);
        var solarMinutes = hours * 60 + eqTime + 4 * longitude - 60 * utcOffset;
        var hourAngle = (solarMinutes / 4 - 180) * Math.PI / 180;
        var lat = latitude * Math.PI / 180;

        var east = -Math.Cos(decl) * Math.Sin(hourAngle);
        var north = Math.Sin(decl) * Math.Cos(lat) - Math.Cos(decl) * Math.Cos(hourAngle) * Math.Sin(lat);
        var up = Math.Sin(decl) * Math.Sin(lat) + Math.Cos(decl) * Math.Cos(hourAngle) * Math.Cos(lat);

        var a = northAngle * Math.PI / 180;
        return new Vec3(east * Math.Cos(a) + north * Math.Sin(a), -east * Math.Sin(a) + north * Math.Cos(a), up).Normalized();
    }
}
