using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Number and unit parsing: "&lt;number&gt;[unit]" as written in a list.</summary>
  public class UnitsTests
  {
    [Theory]
    [InlineData(".84in", 0.84, LengthUnit.Inches)]
    [InlineData("1.68in", 1.68, LengthUnit.Inches)]
    [InlineData("10 mm", 10.0, LengthUnit.Millimeters)]
    [InlineData("1.5cm", 1.5, LengthUnit.Centimeters)]
    [InlineData("2m", 2.0, LengthUnit.Meters)]
    [InlineData("3ft", 3.0, LengthUnit.Feet)]
    [InlineData("4 inches", 4.0, LengthUnit.Inches)]
    [InlineData("5 Millimeters", 5.0, LengthUnit.Millimeters)]
    [InlineData("6\"", 6.0, LengthUnit.Inches)]
    [InlineData("7'", 7.0, LengthUnit.Feet)]
    [InlineData("-2.5in", -2.5, LengthUnit.Inches)]
    [InlineData("  8  ", 8.0, LengthUnit.Model)]
    public void ParsesNumberAndUnit(string text, double expectedValue, LengthUnit expectedUnit)
    {
      Assert.True(Units.TryParse(text, out var measure, out var unknownUnit));
      Assert.Equal(expectedValue, measure.Value, 9);
      Assert.Equal(expectedUnit, measure.Unit);
      Assert.Null(unknownUnit);
    }

    [Fact]
    public void MissingUnitResolvesToModelUnits()
    {
      Assert.True(Units.TryParse("2", out var measure, out var unknownUnit));
      Assert.Equal(LengthUnit.Model, measure.Unit);
      Assert.Null(unknownUnit);
    }

    [Fact]
    public void UnknownUnitIsReportedAndFallsBackToModelUnits()
    {
      Assert.True(Units.TryParse("5 furlongs", out var measure, out var unknownUnit));
      Assert.Equal(5.0, measure.Value);
      Assert.Equal(LengthUnit.Model, measure.Unit);
      Assert.Equal("furlongs", unknownUnit);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1,2")]
    [InlineData("in")]
    public void RejectsValuesWithoutANumber(string text)
      => Assert.False(Units.TryParse(text, out _, out _));

    [Theory]
    [InlineData(LengthUnit.Inches, "in")]
    [InlineData(LengthUnit.Millimeters, "mm")]
    [InlineData(LengthUnit.Centimeters, "cm")]
    [InlineData(LengthUnit.Meters, "m")]
    [InlineData(LengthUnit.Feet, "ft")]
    [InlineData(LengthUnit.Model, "")]
    public void SuffixRoundTripsThroughTheParser(LengthUnit unit, string expectedSuffix)
    {
      Assert.Equal(expectedSuffix, Units.Suffix(unit));

      var written = new Measure(1.25, unit).ToString();
      Assert.True(Units.TryParse(written, out var reparsed, out _));
      Assert.Equal(1.25, reparsed.Value, 9);
      Assert.Equal(unit, reparsed.Unit);
    }

    [Fact]
    public void MeasureFormatsWithInvariantDecimalPoint()
      => Assert.Equal("0.84in", new Measure(0.84, LengthUnit.Inches).ToString());

    [Theory]
    [InlineData(LengthUnit.Inches, 0.0254)]
    [InlineData(LengthUnit.Millimeters, 0.001)]
    [InlineData(LengthUnit.Centimeters, 0.01)]
    [InlineData(LengthUnit.Meters, 1.0)]
    [InlineData(LengthUnit.Feet, 0.3048)]
    public void KnownUnitsHaveAPhysicalLength(LengthUnit unit, double expectedMeters)
      => Assert.Equal(expectedMeters, Units.MetersPerUnit(unit), 9);

    [Fact]
    public void ModelUnitsHaveNoFixedLength()
      => Assert.True(double.IsNaN(Units.MetersPerUnit(LengthUnit.Model)));
  }
}
