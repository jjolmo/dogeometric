using Dogeometric.Core.Units;

namespace Dogeometric.Core.Tests;

public class LengthTests
{
    [Theory]
    [InlineData("12", LengthUnit.Millimeters, 12)]
    [InlineData("12.5", LengthUnit.Millimeters, 12.5)]
    [InlineData("12,5", LengthUnit.Millimeters, 12.5)]
    [InlineData("3", LengthUnit.Centimeters, 30)]
    [InlineData("1.5cm", LengthUnit.Millimeters, 15)]
    [InlineData("2 m", LengthUnit.Millimeters, 2000)]
    [InlineData("4mm", LengthUnit.Meters, 4)]
    [InlineData("-3", LengthUnit.Millimeters, -3)]
    [InlineData(" 7MM ", LengthUnit.Meters, 7)]
    public void Parses_metric_lengths(string text, LengthUnit unit, double expected)
    {
        Assert.True(Length.TryParse(text, unit, out var mm));
        Assert.Equal(expected, mm, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("mm")]
    public void Rejects_invalid_text(string text)
    {
        Assert.False(Length.TryParse(text, LengthUnit.Millimeters, out _));
    }

    [Fact]
    public void Formats_with_unit_and_precision()
    {
        Assert.Equal("12.50mm", Length.Format(12.5, LengthUnit.Millimeters, 2));
        Assert.Equal("1.3cm", Length.Format(12.5, LengthUnit.Centimeters, 1));
        Assert.Equal("0mm", Length.Format(-0.0001, LengthUnit.Millimeters, 0));
    }

    [Theory]
    [InlineData("5'", 1524)]
    [InlineData("6\"", 152.4)]
    [InlineData("5'6\"", 1676.4)]
    [InlineData("5'-6 1/2\"", 1689.1)]
    [InlineData("5' 6", 1676.4)]
    [InlineData("3/4\"", 19.05)]
    [InlineData("2ft", 609.6)]
    [InlineData("7in", 177.8)]
    [InlineData("-1'", -304.8)]
    public void Imperial_lengths_parse(string text, double mm)
    {
        Assert.True(Length.TryParse(text, LengthUnit.Millimeters, out var v));
        Assert.Equal(mm, v, 6);
    }

    [Fact]
    public void A_bare_number_uses_the_format_s_input_unit()
    {
        Assert.True(Length.TryParse("2 1/2", new UnitSettings(UnitFormat.Fractional).InputUnit, out var v));
        Assert.Equal(63.5, v, 6);
        Assert.True(Length.TryParse("2", new UnitSettings(UnitFormat.Engineering).InputUnit, out v));
        Assert.Equal(609.6, v, 6);
    }

    [Fact]
    public void The_four_formats_show_lengths_as_SketchUp_does()
    {
        var mm = 14.5 * 25.4;
        Assert.Equal("1'-2 1/2\"", Length.Format(mm, new UnitSettings(UnitFormat.Architectural, Precision: 4)));
        Assert.Equal("14 1/2\"", Length.Format(mm, new UnitSettings(UnitFormat.Fractional, Precision: 4)));
        Assert.Equal("1.21'", Length.Format(mm, new UnitSettings(UnitFormat.Engineering, Precision: 2)));
        Assert.Equal("14.5\"", Length.Format(mm, new UnitSettings(UnitFormat.Decimal, LengthUnit.Inches, 1)));
        Assert.Equal("368.3mm", Length.Format(mm, new UnitSettings(UnitFormat.Decimal, LengthUnit.Millimeters, 1)));
        // Under a foot shows inches alone, unless 0' is forced; a whole number of feet shows feet alone.
        Assert.Equal("6\"", Length.Format(152.4, new UnitSettings(UnitFormat.Architectural)));
        Assert.Equal("0'-6\"", Length.Format(152.4, new UnitSettings(UnitFormat.Architectural, ForceZeroFeet: true)));
        Assert.Equal("2'", Length.Format(609.6, new UnitSettings(UnitFormat.Architectural)));
        Assert.Equal("4'-0 5/8\"", Length.Format(1234.5678, new UnitSettings(UnitFormat.Architectural, Precision: 4)));
        Assert.Equal("368.3", Length.Format(mm, new UnitSettings(ShowSymbol: false)));
    }
}
