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
    [InlineData("12in")]
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
}
