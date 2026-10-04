using System.Globalization;

namespace Dogeometric.Core.Units;

public enum LengthUnit
{
    Millimeters,
    Centimeters,
    Meters,
}

/// <summary>
/// Metric length parsing and formatting for the Measurements box. Internal unit is the millimetre.
/// </summary>
public static class Length
{
    /// <summary>
    /// Parses "12", "12.5", "12mm", "1.5cm", "2 m", "-3". A bare number uses <paramref name="defaultUnit"/>,
    /// as SketchUp uses the model unit. Accepts both '.' and ',' as decimal separator when it is the only separator.
    /// </summary>
    public static bool TryParse(string text, LengthUnit defaultUnit, out double millimeters)
    {
        millimeters = 0;
        var s = text.Trim().ToLowerInvariant().Replace(" ", "");
        if (s.Length == 0)
            return false;

        var unit = defaultUnit;
        foreach (var (suffix, u) in Suffixes)
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                unit = u;
                s = s[..^suffix.Length];
                break;
            }
        }

        s = s.Replace(',', '.');
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value))
            return false;

        millimeters = value * ToMillimeters(unit);
        return true;
    }

    public static string Format(double millimeters, LengthUnit unit, int decimals)
    {
        var rounded = Math.Round(millimeters / ToMillimeters(unit), decimals, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("F" + decimals, CultureInfo.InvariantCulture);
        if (text.StartsWith('-') && rounded == 0)
            text = text[1..];
        return $"{text}{Symbol(unit)}";
    }

    public static double ToMillimeters(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeters => 1,
        LengthUnit.Centimeters => 10,
        LengthUnit.Meters => 1000,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static string Symbol(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeters => "mm",
        LengthUnit.Centimeters => "cm",
        LengthUnit.Meters => "m",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    // Longest suffix first so "mm" is not read as "m".
    private static readonly (string Suffix, LengthUnit Unit)[] Suffixes =
    [
        ("mm", LengthUnit.Millimeters),
        ("cm", LengthUnit.Centimeters),
        ("m", LengthUnit.Meters),
    ];
}
