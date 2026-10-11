using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Arithmetic in numeric values: ".31/2", "2in/2", "(12.7 + 12.7)mm".</summary>
  [Trait("suite", "unit")]
  public class ArithmeticTests
  {
    static Part SinglePart(string body)
      => Assert.Single(ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body).AllParts);

    // ---- The evaluator --------------------------------------------------------------

    [Theory]
    [InlineData(".31/2", 0.155, LengthUnit.Model)]
    [InlineData(".31in/2", 0.155, LengthUnit.Inches)]
    [InlineData("(1+2)/4 mm", 0.75, LengthUnit.Millimeters)]
    [InlineData("(12.7 + 12.7)mm", 25.4, LengthUnit.Millimeters)]
    [InlineData("25.4mm * 1", 25.4, LengthUnit.Millimeters)]
    [InlineData("1 + 2 * 3", 7.0, LengthUnit.Model)]
    [InlineData("(1 + 2) * 3", 9.0, LengthUnit.Model)]
    [InlineData("10 - 4 - 3", 3.0, LengthUnit.Model)]
    [InlineData("12 / 3 / 2", 2.0, LengthUnit.Model)]
    [InlineData("-(-1)", 1.0, LengthUnit.Model)]
    [InlineData("-2in * 3", -6.0, LengthUnit.Inches)]
    [InlineData("1in + 2", 3.0, LengthUnit.Inches)]
    [InlineData("1e1 / 4", 2.5, LengthUnit.Model)]
    public void EvaluatesExpressions(string text, double expected, LengthUnit expectedUnit)
    {
      Assert.True(Units.TryParse(text, out var measure, out var unknownUnit, out var error));
      Assert.Equal(expected, measure.Value, 9);
      Assert.Equal(expectedUnit, measure.Unit);
      Assert.Null(unknownUnit);
      Assert.Null(error);
    }

    [Fact]
    public void ZeroIsNeverWrittenAsNegative()
    {
      Assert.True(Units.TryParse("-(2 - 2)", out var measure, out _));
      Assert.Equal("0", measure.ToString());
    }

    [Theory]
    [InlineData("1in + 2mm", "mixed units")]
    [InlineData("2in * 3in", "area")]
    [InlineData("4mm / 2mm", "ratio")]
    [InlineData("1 / 0", "division by zero")]
    [InlineData("1 / (2 - 2)", "division by zero")]
    public void ArithmeticThatCannotBeALengthSaysWhy(string text, string reason)
    {
      Assert.False(Units.TryParse(text, out _, out _, out var error));
      Assert.Contains(reason, error);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("1 2")]
    [InlineData("in / 2")]
    [InlineData("2 ^ 3")]
    [InlineData("tall")]
    public void TextThatIsNotArithmeticIsRejectedWithoutAReason(string text)
    {
      Assert.False(Units.TryParse(text, out _, out _, out var error));
      Assert.Null(error);
    }

    [Fact]
    public void AnUnknownUnitInsideArithmeticIsReportedLikeASingleNumber()
    {
      Assert.True(Units.TryParse("4 furlongs / 2", out var measure, out var unknownUnit, out _));
      Assert.Equal(2.0, measure.Value, 9);
      Assert.Equal(LengthUnit.Model, measure.Unit);
      Assert.Equal("furlongs", unknownUnit);
    }

    // ---- In a list ------------------------------------------------------------------

    [Fact]
    public void DimensionsAcceptArithmetic()
    {
      var part = SinglePart("- radius: .31/2\n- distance: .39\n- position: -.65, -.04, .04\n");
      Assert.Equal(0.155, part.Width.Value.Value, 9);
      Assert.Equal(0.155, part.Height.Value.Value, 9);
      Assert.Equal(0.39, part.Depth.Value.Value, 9);
      Assert.Equal(0.04, part.Offset.Value, 9);
      Assert.False(part.HasError);
    }

    [Theory]
    [InlineData("410, -(2 - 2)", 410.0, 0.0)]
    [InlineData("(1 + 1), 3", 2.0, 3.0)]
    [InlineData("10/2, 6/3", 5.0, 2.0)]
    [InlineData("(3, 4)", 3.0, 4.0)]
    [InlineData("[5, 6]", 5.0, 6.0)]
    public void PositionsAcceptArithmetic(string text, double expectedA, double expectedB)
    {
      var part = SinglePart("- height: 1\n- width: 1\n- depth: 1\n- position: " + text + "\n");
      Assert.Equal(expectedA, part.PositionA.Value, 9);
      Assert.Equal(expectedB, part.PositionB.Value, 9);
      Assert.False(part.HasError);
    }

    [Fact]
    public void ArithmeticThatFailsIsAnErrorThatSaysWhy()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n- height: 1/0\n- width: 1\n- depth: 1\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error
        && d.Message.Contains("Cannot read height") && d.Message.Contains("division by zero"));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void AnUnreadableOptionalValueAlsoSkipsThePart()
    {
      // An unreadable number is an error, and an error means the part is skipped, even
      // when the value is optional and would otherwise fall back to a default.
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n- height: 1\n- width: 1\n- depth: 1\n- offset: 2in * 3in\n");
      Assert.True(doc.HasErrors);
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void TheWriterWritesTheEvaluatedNumber()
    {
      var written = ListWriter.Write(ListParser.Parse("# Element: Boolean\n\n## Knob\n\n- radius: .31/2\n- depth: 2in/2\n"));
      Assert.Contains("- radius: 0.155", written);
      Assert.Contains("- depth:  1in", written);
    }
  }
}
