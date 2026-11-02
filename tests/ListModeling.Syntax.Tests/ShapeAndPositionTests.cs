using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Squares, and world coordinates written with x-, y- and z-position.</summary>
  [Trait("suite", "unit")]
  public class ShapeAndPositionTests
  {
    static ListDocument Parse(string body) => ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);

    static Part SinglePart(string body) => Assert.Single(Parse(body).AllParts);

    static string Normalize(string text) => text.Replace("\r\n", "\n");

    // ---- Squares ----------------------------------------------------------------------

    [Theory]
    [InlineData("- width: 2\n")]
    [InlineData("- height: 2\n")]
    [InlineData("- width: 2\n- height: 2\n")]
    public void ASquareTakesOneSideForBothAxes(string side)
    {
      var doc = Parse("- shape: square\n" + side + "- depth: 1\n");
      Assert.Empty(doc.Diagnostics);
      var part = Assert.Single(doc.AllParts);
      Assert.Equal(PartShape.Square, part.Shape);
      Assert.Equal(2.0, part.Width.Value.Value, 9);
      Assert.Equal(2.0, part.Height.Value.Value, 9);
    }

    [Fact]
    public void ASquareKeepsTheUnitOfItsSide()
    {
      var part = SinglePart("- shape: square\n- height: 5mm\n- depth: 1\n");
      Assert.Equal(LengthUnit.Millimeters, part.Width.Value.Unit);
    }

    [Fact]
    public void ASquareWithNoSideIsMissingItsWidth()
    {
      var doc = Parse("- shape: square\n- depth: 1\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.EndsWith("is missing: width."));
    }

    [Fact]
    public void ASquareIsWrittenWithItsShapeAndOneSide()
    {
      var written = Normalize(ListWriter.Write(Parse("- shape: square\n- height: 2\n- depth: 1\n")));
      Assert.Contains("- shape: square\n- width:  2\n- depth:  1\n", written);
      Assert.DoesNotContain("height", written);
    }

    // ---- Axis positions -----------------------------------------------------------------

    /// <summary>x 1, y 2, z 3 written as axis positions, read back on each orientation's own frame.</summary>
    [Theory]
    [InlineData("top", 1, 2, 3)]
    [InlineData("bottom", 1, 2, -3)]
    [InlineData("front", 1, 3, 2)]
    [InlineData("back", 1, 3, -2)]
    [InlineData("right", 2, 3, 1)]
    [InlineData("left", 2, 3, -1)]
    public void AxisPositionsLandOnTheOrientationsOwnCoordinates(string orientation, double a, double b, double offset)
    {
      var part = SinglePart(
        "- orientation: " + orientation + "\n- height: 1\n- width: 1\n- depth: 1\n" +
        "- x-position: 1\n- y-position: 2\n- z-position: 3\n");

      Assert.Equal(a, part.PositionA.Value, 9);
      Assert.Equal(b, part.PositionB.Value, 9);
      Assert.Equal(offset, part.Offset.Value, 9);
    }

    [Fact]
    public void TheOrientationMayComeAfterTheCoordinates()
      => Assert.Equal(-3.0, SinglePart("- z-position: 3\n- orientation: bottom\n- height: 1\n- width: 1\n- depth: 1\n").Offset.Value, 9);

    [Fact]
    public void ShortKeysAndUnitsAreRead()
    {
      var part = SinglePart("- orientation: bottom\n- height: 1\n- width: 1\n- depth: 1\n- xpos: 4in\n- z pos: 2mm\n");
      Assert.Equal(4.0, part.PositionA.Value, 9);
      Assert.Equal(LengthUnit.Inches, part.PositionA.Unit);
      Assert.Equal(-2.0, part.Offset.Value, 9);
      Assert.Equal(LengthUnit.Millimeters, part.Offset.Unit);
    }

    [Fact]
    public void AZeroWorldCoordinateOnANegativeAxisStaysZero()
    {
      var written = ListWriter.Write(Parse("- orientation: left\n- height: 1\n- width: 1\n- depth: 1\n- x-position: 0\n"));
      Assert.DoesNotContain("offset", written);
      Assert.DoesNotContain("-0", written);
    }

    [Theory]
    [InlineData("- z-position: 9\n- position: 1, 1, 2\n", 2)]
    [InlineData("- position: 1, 1, 2\n- z-position: 4\n", 4)]
    [InlineData("- offset: 6\n- z-position: 4\n", 4)]
    [InlineData("- z-position: 4\n- offset: 6\n", 6)]
    public void UnderOneHeaderTheLaterLineWins(string lines, double offset)
      => Assert.Equal(offset, SinglePart("- height: 1\n- width: 1\n- depth: 1\n" + lines).Offset.Value, 9);

    [Theory]
    [InlineData("- position: 635, 0, 2\n", 2)]
    [InlineData("- offset: 4\n", 4)]
    [InlineData("- position: 635, 0\n", 9)]
    public void ThePartsOwnLineBeatsTheContainersAxisPosition(string own, double offset)
    {
      var part = Assert.Single(ListParser.Parse(
        "# Element: Boolean\n\n## Row\n\n- height: 1\n- width: 1\n- depth: 1\n- z-position: 9\n\n### Cube\n\n" + own).AllParts);
      Assert.Equal(offset, part.Offset.Value, 9);
    }

    [Fact]
    public void AnEmptyAxisPositionFallsBackToTheContext()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n- z-position: 7\n\n" +
        "## Row\n\n- height: 1\n- width: 1\n- depth: 1\n- z-position: 9\n\n" +
        "### Cleared\n\n- z-position:\n\n" +
        "# Element: Boolean\n\n## Row Two\n\n- height: 1\n- width: 1\n- depth: 1\n- z-position: 9\n\n" +
        "### Back To Zero\n\n- z-position:\n");

      Assert.Empty(doc.Diagnostics);
      Assert.Equal(7.0, doc.Elements[0].Parts.Single().Offset.Value, 9);
      Assert.Equal(0.0, doc.Elements[1].Parts.Single().Offset.Value, 9);
    }

    [Fact]
    public void AnUnreadableAxisPositionIsAnError()
    {
      var doc = Parse("- height: 1\n- width: 1\n- depth: 1\n- x-position: far\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("Cannot read x-position 'far'"));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void AxisPositionsAreWrittenAsPositionAndOffset()
    {
      var written = Normalize(ListWriter.Write(Parse(
        "- orientation: front\n- height: 1\n- width: 1\n- depth: 1\n- x-position: 1\n- y-position: 2\n- z-position: 3\n")));

      Assert.Contains("- position: 1, 3\n- offset: 2\n", written);
      Assert.DoesNotContain("-position", written);
    }
  }
}
