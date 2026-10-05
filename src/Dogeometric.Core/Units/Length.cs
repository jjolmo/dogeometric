using System.Globalization;

namespace Dogeometric.Core.Units;

public enum LengthUnit
{
    Millimeters,
    Centimeters,
    Meters,
    Inches,
    Feet,
}

/// <summary>Model Info › Units › Format, as SketchUp has them.</summary>
public enum UnitFormat
{
    /// <summary>Feet and inches with fractions: 1'-2 1/2".</summary>
    Architectural,
    /// <summary>Any unit with decimals: 345.6mm, 14.5".</summary>
    Decimal,
    /// <summary>Decimal feet: 1.21'.</summary>
    Engineering,
    /// <summary>Inches with fractions: 14 1/2".</summary>
    Fractional,
}

/// <summary>
/// The model's length display: format, unit (decimal format only), precision (decimals, or the fraction's
/// denominator 2^precision), whether the unit symbol shows, and whether architectural lengths under a foot show 0'.
/// </summary>
public sealed record UnitSettings(UnitFormat Format = UnitFormat.Decimal, LengthUnit Unit = LengthUnit.Millimeters, int Precision = 1,
    bool ShowSymbol = true, bool ForceZeroFeet = false)
{
    /// <summary>The unit a bare typed number is read in.</summary>
    public LengthUnit InputUnit => Format switch
    {
        UnitFormat.Architectural or UnitFormat.Fractional => LengthUnit.Inches,
        UnitFormat.Engineering => LengthUnit.Feet,
        _ => Unit,
    };
}

/// <summary>
/// Length parsing and formatting for the Measurements box, metric and imperial. Internal unit is the millimetre.
/// </summary>
public static class Length
{
    private const double Inch = 25.4;

    /// <summary>
    /// Parses "12", "12.5", "12mm", "1.5cm", "2 m", "-3", and imperial lengths: 5', 6", 5'6", 5'-6 1/2", 3/4", 2ft,
    /// 7in. A bare number uses <paramref name="defaultUnit"/>, as SketchUp uses the model unit. Accepts ',' as the
    /// decimal separator when it is the only separator.
    /// </summary>
    public static bool TryParse(string text, LengthUnit defaultUnit, out double millimeters)
    {
        millimeters = 0;
        var s = text.Trim().ToLowerInvariant();
        if (s.Length == 0)
            return false;
        var sign = 1;
        if (s[0] is '-' or '+')
        {
            sign = s[0] == '-' ? -1 : 1;
            s = s[1..].TrimStart();
        }

        // Feet and inches: what precedes ' (or ft) is feet, the rest inches.
        var feetMark = s.IndexOf('\'');
        var feetWord = feetMark < 0 ? IndexOfWord(s, "feet", "foot", "ft") : (Index: -1, Length: 0);
        if (feetMark >= 0 || feetWord.Index >= 0)
        {
            var (at, length) = feetMark >= 0 ? (feetMark, 1) : feetWord;
            if (!TryNumber(s[..at], out var feet))
                return false;
            var rest = s[(at + length)..].Trim().TrimStart('-').Trim();
            var inches = 0.0;
            if (rest.Length > 0 && !TryNumber(StripInches(rest), out inches))
                return false;
            millimeters = sign * (feet * 12 + inches) * Inch;
            return true;
        }
        if (s.EndsWith('"') || IndexOfWord(s, "inches", "inch", "in").Index >= 0)
        {
            if (!TryNumber(StripInches(s), out var inches))
                return false;
            millimeters = sign * inches * Inch;
            return true;
        }

        var unit = defaultUnit;
        foreach (var (suffix, u) in Suffixes)
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                unit = u;
                s = s[..^suffix.Length].TrimEnd();
                break;
            }
        }
        if (!TryNumber(s, out var value))
            return false;
        millimeters = sign * value * ToMillimeters(unit);
        return true;
    }

    /// <summary>The last of the words that ends the text (after a number), as its index and length.</summary>
    private static (int Index, int Length) IndexOfWord(string s, params string[] words)
    {
        foreach (var w in words)
            if (s.EndsWith(w, StringComparison.Ordinal) && s.Length > w.Length && (char.IsDigit(s[^(w.Length + 1)]) || s[^(w.Length + 1)] == ' '))
                return (s.Length - w.Length, w.Length);
        foreach (var w in words)
        {
            var i = s.IndexOf(w, StringComparison.Ordinal);
            if (i > 0 && (char.IsDigit(s[i - 1]) || s[i - 1] == ' ') && (i + w.Length == s.Length || !char.IsLetter(s[i + w.Length])))
                return (i, w.Length);
        }
        return (-1, 0);
    }

    private static string StripInches(string s)
    {
        s = s.Trim();
        foreach (var w in new[] { "\"", "inches", "inch", "in" })
            if (s.EndsWith(w, StringComparison.Ordinal))
                return s[..^w.Length].Trim();
        return s;
    }

    /// <summary>"3.5", "3,5", "1/2", "3 1/2" or "3-1/2".</summary>
    private static bool TryNumber(string text, out double value)
    {
        value = 0;
        var s = text.Trim();
        if (s.Length == 0)
            return false;
        var slash = s.IndexOf('/');
        if (slash < 0)
        {
            s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        // A fraction, after an optional whole number.
        var split = s.LastIndexOfAny([' ', '-'], slash);
        var whole = 0.0;
        if (split > 0 && !double.TryParse(s[..split], NumberStyles.Float, CultureInfo.InvariantCulture, out whole))
            return false;
        var fraction = s[(split + 1)..].Split('/');
        if (fraction.Length != 2 || !double.TryParse(fraction[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            || !double.TryParse(fraction[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d == 0)
            return false;
        value = whole + n / d;
        return true;
    }

    public static string Format(double millimeters, LengthUnit unit, int decimals) =>
        Format(millimeters, new UnitSettings(UnitFormat.Decimal, unit, decimals));

    public static string Format(double millimeters, UnitSettings u)
    {
        var negative = millimeters < 0;
        var mm = Math.Abs(millimeters);
        string text;
        switch (u.Format)
        {
            case UnitFormat.Architectural:
            {
                var denominator = 1 << Math.Clamp(u.Precision, 0, 6);
                var parts = (long)Math.Round(mm / Inch * denominator, MidpointRounding.AwayFromZero);
                var feet = parts / (12 * denominator);
                var inches = Fraction(parts - feet * 12 * denominator, denominator, wholeZero: feet > 0 || u.ForceZeroFeet);
                text = feet > 0 || u.ForceZeroFeet ? (inches == "0" ? $"{feet}'" : $"{feet}'-{inches}\"") : $"{inches}\"";
                if (!u.ShowSymbol)
                    text = text.Replace("\"", "");
                break;
            }
            case UnitFormat.Fractional:
            {
                var denominator = 1 << Math.Clamp(u.Precision, 0, 6);
                var parts = (long)Math.Round(mm / Inch * denominator, MidpointRounding.AwayFromZero);
                text = Fraction(parts, denominator) + (u.ShowSymbol ? "\"" : "");
                break;
            }
            case UnitFormat.Engineering:
                text = Decimal(mm / ToMillimeters(LengthUnit.Feet), u.Precision) + (u.ShowSymbol ? "'" : "");
                break;
            default:
                text = Decimal(mm / ToMillimeters(u.Unit), u.Precision) + (u.ShowSymbol ? Symbol(u.Unit) : "");
                break;
        }
        return negative && text.Any(c => c is >= '1' and <= '9') ? "-" + text : text;
    }

    private static string Decimal(double value, int decimals) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero).ToString("F" + Math.Clamp(decimals, 0, 6), CultureInfo.InvariantCulture);

    /// <summary>"14 1/2" from 29 halves: whole part and the reduced fraction.</summary>
    private static string Fraction(long parts, int denominator, bool wholeZero = false)
    {
        var whole = parts / denominator;
        var n = parts % denominator;
        if (n == 0)
            return whole.ToString(CultureInfo.InvariantCulture);
        var d = denominator;
        while (n % 2 == 0)
        {
            n /= 2;
            d /= 2;
        }
        return whole > 0 || wholeZero ? $"{whole} {n}/{d}" : $"{n}/{d}";
    }

    public static double ToMillimeters(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeters => 1,
        LengthUnit.Centimeters => 10,
        LengthUnit.Meters => 1000,
        LengthUnit.Inches => Inch,
        LengthUnit.Feet => 12 * Inch,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static string Symbol(LengthUnit unit) => unit switch
    {
        LengthUnit.Millimeters => "mm",
        LengthUnit.Centimeters => "cm",
        LengthUnit.Meters => "m",
        LengthUnit.Inches => "\"",
        LengthUnit.Feet => "'",
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
