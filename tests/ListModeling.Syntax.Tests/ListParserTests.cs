using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Structure and property parsing for the Markdown list format.</summary>
  [Trait("suite", "unit")]
  public class ListParserTests
  {
    static Part SinglePart(string body)
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);
      return Assert.Single(doc.AllParts);
    }

    /// <summary>A part with every required dimension, so property tests do not trip validation.</summary>
    const string Dimensions = "* height: 1\n* width: 2\n* depth: 3\n";

    // ---- Structure --------------------------------------------------------------

    [Fact]
    public void ReadsElementsPartsAndProperties()
    {
      var doc = ListParser.Parse(
        "# Element: BooleanDifference\n\n" +
        "## Circuit Board\n\n" +
        "* LayerName: Circuit_Board\n" +
        "* LayerColor: Dark Blue\n" +
        "* orientation: top\n" +
        "* height: .84in\n" +
        "* width:  1.68in\n" +
        "* depth:  .04in\n" +
        "* position: 0,0\n" +
        "* actions: extrude\n");

      var element = Assert.Single(doc.Elements);
      Assert.Equal("Element", element.Kind);
      Assert.Equal("BooleanDifference", element.Type);
      Assert.Equal(ElementFamily.Solid, element.Family);

      var part = Assert.Single(element.Parts);
      Assert.Equal("Circuit Board", part.Name);
      Assert.Equal("Circuit_Board", part.LayerName);
      Assert.Equal("Dark Blue", part.LayerColor);
      Assert.Equal(Orientation.Top, part.Orientation);
      Assert.Equal(0.84, part.Height.Value.Value, 9);
      Assert.Equal(LengthUnit.Inches, part.Height.Value.Unit);
      Assert.Equal(1.68, part.Width.Value.Value, 9);
      Assert.Equal(0.04, part.Depth.Value.Value, 9);
      Assert.Equal(PartAction.Extrude, part.PrimaryAction);
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void ElementHeaderWithoutTheKindPrefixStillNamesTheType()
    {
      var doc = ListParser.Parse("# BooleanUnion\n\n## Part\n\n" + Dimensions);
      Assert.Equal("BooleanUnion", doc.Elements[0].Type);
      Assert.Equal(ElementFamily.Union, doc.Elements[0].Family);
    }

    [Fact]
    public void PartsBeforeAnyElementHeaderAssumeBoolean()
    {
      var doc = ListParser.Parse("## Part\n\n" + Dimensions);
      Assert.Equal("Boolean", Assert.Single(doc.Elements).Type);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Info && d.Message.Contains("Element: Boolean"));
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void PropertiesBeforeAnyPartAreIgnoredWithAWarning()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n* height: 1\n\n## Part\n\n" + Dimensions);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("before any"));
    }

    [Fact]
    public void GroupedPartsShareTheParentNameAndCarryTheGroupId()
    {
      var doc = ListParser.Parse(
        "# Element: BooleanDifference\n\n" +
        "## Mount Holes\n\n" +
        "### group:holes\n\n" +
        "- shape: circle\n- radius: .05in\n- depth: 1in\n- position: .62, -.35\n- actions: subtract\n\n" +
        "### group:holes\n\n" +
        "- shape: circle\n- radius: .05in\n- depth: 1in\n- position: -.62, .35\n- actions: subtract\n");

      var parts = doc.AllParts.ToList();
      Assert.Equal(2, parts.Count);
      Assert.All(parts, p => Assert.Equal("holes", p.Group));
      Assert.All(parts, p => Assert.Equal("Mount Holes", p.Name));
      Assert.All(parts, p => Assert.Equal(PartAction.Subtract, p.PrimaryAction));
      Assert.Equal("Mount Holes [group:holes]", parts[0].DisplayName);
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void AnEmptyContainerHeaderAboveGroupsIsNotItselfAPart()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Holes\n\n### group:h\n\n" + Dimensions);
      Assert.Single(doc.AllParts);
      Assert.Equal("h", doc.AllParts.First().Group);
    }

    [Fact]
    public void AContainerWithItsOwnDimensionsStillOnlyProvidesDefaults()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Plate\n\n" + Dimensions + "\n### group:g\n\n* position: 5, 5\n");

      // The container's dimensions describe its children, not an extra solid at 0,0.
      var part = Assert.Single(doc.AllParts);
      Assert.Equal("g", part.Group);
      Assert.Equal(1.0, part.Height.Value.Value, 9);
      Assert.Equal(5.0, part.PositionA.Value, 9);
    }

    [Fact]
    public void GroupedPartsInheritWhatTheContainerDeclared()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n" +
        "## Pins\n\n" +
        "* LayerName: Pins\n* orientation: front\n* shape: circle\n* radius: 2\n* depth: 5\n\n" +
        "### group:pins\n\n" +
        "- position: 1, 1\n");

      var part = Assert.Single(doc.AllParts);
      Assert.Equal("pins", part.Group);
      Assert.Equal("Pins", part.LayerName);
      Assert.Equal(Orientation.Front, part.Orientation);
      Assert.Equal(PartShape.Circle, part.Shape);
      Assert.Equal(2.0, part.Width.Value.Value, 9);
      Assert.Equal(1.0, part.PositionA.Value, 9);
    }

    [Fact]
    public void ALevelThreeHeaderThatIsNotAGroupNamesItsOwnPart()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Assembly\n\n### Bracket\n\n" + Dimensions);
      var part = Assert.Single(doc.AllParts);
      Assert.Equal("Bracket", part.Name);
      Assert.Null(part.Group);
    }

    // ---- Line shapes ------------------------------------------------------------

    [Theory]
    [InlineData("*")]
    [InlineData("-")]
    [InlineData("+")]
    public void AcceptsEveryUnorderedBulletStyle(string bullet)
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Part\n\n" +
        bullet + " height: 1\n" + bullet + " width: 2\n" + bullet + " depth: 3\n");
      Assert.False(doc.HasErrors);
      Assert.Equal(1.0, Assert.Single(doc.AllParts).Height.Value.Value, 9);
    }

    [Theory]
    [InlineData("LayerName")]
    [InlineData("layername")]
    [InlineData("layer name")]
    [InlineData("Layer_Name")]
    [InlineData("layer-name")]
    public void PropertyKeysIgnoreCaseSpacesAndSeparators(string key)
      => Assert.Equal("Deck", SinglePart(Dimensions + "* " + key + ": Deck\n").LayerName);

    [Theory]
    [InlineData("()-> a shorthand note")]
    [InlineData("()=> another note")]
    [InlineData("<!-- an html comment -->")]
    [InlineData("```markdown")]
    [InlineData("~~~")]
    public void SkipsCommentAndFenceLinesWithoutComplaint(string line)
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + line + "\n" + Dimensions);
      Assert.False(doc.HasErrors);
      Assert.DoesNotContain(doc.Diagnostics, d => d.Message.StartsWith("Ignored"));
    }

    [Fact]
    public void StripsTrailingShorthandAnnotationsFromHeadersAndValues()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n" +
        "## Board (annotated)-> used for the base\n\n" +
        "* LayerName: null (optional)->could be left out as used to subtract from board\n" +
        Dimensions);

      var part = Assert.Single(doc.AllParts);
      Assert.Equal("Board", part.Name);
      Assert.Null(part.LayerName);
    }

    [Fact]
    public void KeepsParenthesesThatAreNotAnnotations()
      => Assert.Equal("rgb(31, 42, 68)", SinglePart(Dimensions + "* LayerColor: rgb(31, 42, 68)\n").LayerColor);

    [Fact]
    public void StripsBackticksAroundHeadersAndValues()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## `Board`\n\n* LayerName: `Deck`\n" + Dimensions);
      var part = Assert.Single(doc.AllParts);
      Assert.Equal("Board", part.Name);
      Assert.Equal("Deck", part.LayerName);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("NONE")]
    [InlineData("")]
    public void NullAndNoneMeanTheSameAsLeavingAPropertyOut(string value)
      => Assert.Null(SinglePart(Dimensions + "* LayerName: " + value + "\n").LayerName);

    [Fact]
    public void UnknownPropertiesAreKeptForRoundTrippingAndReported()
    {
      var part = SinglePart(Dimensions + "* material: aluminium\n");
      Assert.Equal("aluminium", part.Extra["material"]);
      Assert.Equal("aluminium", part.Extra["MATERIAL"]);
    }

    [Fact]
    public void StrayProseIsIgnoredAndNoted()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\nSome prose about the part.\n" + Dimensions);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Info && d.Message.StartsWith("Ignored"));
      Assert.False(doc.HasErrors);
    }

    // ---- Values -----------------------------------------------------------------

    [Theory]
    [InlineData("top", Orientation.Top)]
    [InlineData("Bottom", Orientation.Bottom)]
    [InlineData("FRONT", Orientation.Front)]
    [InlineData("back", Orientation.Back)]
    [InlineData("right", Orientation.Right)]
    [InlineData("left", Orientation.Left)]
    [InlineData("side", Orientation.Left)]
    public void ReadsEveryOrientationName(string text, Orientation expected)
    {
      Assert.True(ListParser.TryParseOrientation(text, out var orientation));
      Assert.Equal(expected, orientation);
      Assert.Equal(expected, SinglePart(Dimensions + "* orientation: " + text + "\n").Orientation);
    }

    [Fact]
    public void AnUnknownOrientationWarnsAndFallsBackToTop()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* orientation: sideways\n" + Dimensions);
      Assert.Equal(Orientation.Top, Assert.Single(doc.AllParts).Orientation);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("Unknown orientation"));
    }

    [Theory]
    [InlineData("rectangle", PartShape.Rectangle, 6)]
    [InlineData("square", PartShape.Rectangle, 6)]
    [InlineData("circle", PartShape.Circle, 6)]
    [InlineData("ellipse", PartShape.Ellipse, 6)]
    [InlineData("triangle", PartShape.Polygon, 3)]
    [InlineData("pentagon", PartShape.Polygon, 5)]
    [InlineData("hexagon", PartShape.Polygon, 6)]
    [InlineData("octagon", PartShape.Polygon, 8)]
    [InlineData("polygon:5", PartShape.Polygon, 5)]
    [InlineData("polygon(7)", PartShape.Polygon, 7)]
    public void ReadsShapeNames(string text, PartShape expectedShape, int expectedSides)
    {
      var part = SinglePart(Dimensions + "* shape: " + text + "\n");
      Assert.Equal(expectedShape, part.Shape);
      Assert.Equal(expectedSides, part.PolygonSides);
    }

    [Fact]
    public void AnUnknownShapeWarnsAndFallsBackToRectangle()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* shape: blob\n" + Dimensions);
      Assert.Equal(PartShape.Rectangle, Assert.Single(doc.AllParts).Shape);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("Unknown shape"));
    }

    [Fact]
    public void RadiusFillsBothPlaneAxesAndImpliesACircle()
    {
      var part = SinglePart("* radius: 2mm\n* depth: 1\n");
      Assert.Equal(PartShape.Circle, part.Shape);
      Assert.Equal(2.0, part.Width.Value.Value, 9);
      Assert.Equal(2.0, part.Height.Value.Value, 9);
      Assert.Equal(LengthUnit.Millimeters, part.Width.Value.Unit);
    }

    [Fact]
    public void RadiusKeepsAShapeThatWasNamedBeforeIt()
    {
      var part = SinglePart("* shape: hexagon\n* radius: 3\n* depth: 1\n");
      Assert.Equal(PartShape.Polygon, part.Shape);
      Assert.Equal(6, part.PolygonSides);
      Assert.Equal(3.0, part.Width.Value.Value, 9);
    }

    [Fact]
    public void RadiusKeepsAShapeThatIsNamedAfterIt()
    {
      var part = SinglePart("* radius: 4\n* shape: rectangle\n* depth: 1\n");
      Assert.Equal(PartShape.Rectangle, part.Shape);
      Assert.Equal(4.0, part.Width.Value.Value, 9);
      Assert.Equal(4.0, part.Height.Value.Value, 9);
    }

    [Theory]
    [InlineData("0,0", 0.0, 0.0)]
    [InlineData(".62, -.35", 0.62, -0.35)]
    [InlineData("[1, 2]", 1.0, 2.0)]
    [InlineData("(3, 4)", 3.0, 4.0)]
    [InlineData("5 6", 5.0, 6.0)]
    [InlineData("7; 8", 7.0, 8.0)]
    public void ReadsPositionPairs(string text, double expectedA, double expectedB)
    {
      var part = SinglePart(Dimensions + "* position: " + text + "\n");
      Assert.Equal(expectedA, part.PositionA.Value, 9);
      Assert.Equal(expectedB, part.PositionB.Value, 9);
    }

    [Fact]
    public void AThirdPositionValueBecomesTheOffset()
      => Assert.Equal(9.0, SinglePart(Dimensions + "* position: 1, 2, 9\n").Offset.Value, 9);

    [Fact]
    public void APositionWithOneValueWarnsAndFallsBackToTheOrigin()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* position: 5\n" + Dimensions);
      Assert.False(doc.HasErrors);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("two values"));

      var part = Assert.Single(doc.AllParts);
      Assert.Equal(0.0, part.PositionA.Value, 9);
      Assert.Equal(0.0, part.PositionB.Value, 9);
      Assert.False(part.HasError);
    }

    [Theory]
    [InlineData("extrude", PartAction.Extrude)]
    [InlineData("solid", PartAction.Extrude)]
    [InlineData("subtract", PartAction.Subtract)]
    [InlineData("cut", PartAction.Subtract)]
    [InlineData("union", PartAction.Union)]
    [InlineData("join", PartAction.Union)]
    [InlineData("curve", PartAction.Curve)]
    [InlineData("profile", PartAction.Curve)]
    public void ReadsActionNames(string text, PartAction expected)
      => Assert.Equal(expected, SinglePart(Dimensions + "* actions: " + text + "\n").PrimaryAction);

    [Fact]
    public void ReadsSeveralActionsInOneValue()
    {
      var part = SinglePart(Dimensions + "* actions: [extrude, union]\n");
      Assert.Equal(new[] { PartAction.Extrude, PartAction.Union }, part.Actions);
    }

    [Fact]
    public void TheSurfaceActionIsTreatedAsACurveWithAWarning()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* height: 1\n* width: 2\n* actions: surface\n");
      Assert.Equal(PartAction.Curve, Assert.Single(doc.AllParts).PrimaryAction);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("planned"));
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void AnUnknownActionWarnsAndTheDefaultRemains()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + Dimensions + "* actions: sculpt\n");
      Assert.Equal(PartAction.Extrude, Assert.Single(doc.AllParts).PrimaryAction);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("Unknown action"));
    }

    // ---- Validation -------------------------------------------------------------

    [Theory]
    [InlineData("* width: 2\n* depth: 3\n", "height")]
    [InlineData("* height: 1\n* depth: 3\n", "width")]
    [InlineData("* height: 1\n* width: 2\n", "depth")]
    public void AMissingDimensionIsAnError(string body, string expectedName)
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);
      Assert.True(doc.HasErrors);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains(expectedName));

      // The runner reads this flag and skips the part rather than guessing at it.
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void ACurvePartDoesNotNeedADepth()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* height: 1\n* width: 2\n* actions: curve\n");
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void APartThatExtrudesAndAlsoDrawsACurveStillNeedsADepth()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* height: 1\n* width: 2\n* actions: extrude, curve\n");
      Assert.True(doc.HasErrors);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("depth"));
    }

    [Theory]
    [InlineData("* height: 0\n* width: 2\n* depth: 3\n")]
    [InlineData("* height: 1\n* width: -2\n* depth: 3\n")]
    [InlineData("* height: 1\n* width: 2\n* depth: 0\n")]
    public void AZeroOrNegativeDimensionIsAnError(string body)
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);
      Assert.True(doc.HasErrors);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("zero or negative"));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void APartResolvedToDefaultsIsNotFlaggedAsAnError()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Part\n\n" +
        "* LayerColor: chartreusey\n* orientation: sideways\n* shape: blob\n" + Dimensions);

      Assert.False(doc.HasErrors);
      Assert.False(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void AnUnreadableNumberIsAnError()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* height: tall\n* width: 2\n* depth: 3\n");
      Assert.True(doc.HasErrors);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("Cannot read height"));
    }

    [Fact]
    public void AnUnknownUnitWarnsAndUsesModelUnits()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* height: 1 furlong\n* width: 2\n* depth: 3\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("Unknown unit"));
      Assert.Equal(LengthUnit.Model, Assert.Single(doc.AllParts).Height.Value.Unit);
    }

    [Fact]
    public void AnElementWithNoPartsWarns()
    {
      var doc = ListParser.Parse("# Element: Boolean\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("no parts"));
    }

    [Fact]
    public void EveryPartGetsADefaultActionOfExtrude()
      => Assert.Equal(PartAction.Extrude, SinglePart(Dimensions).PrimaryAction);

    [Fact]
    public void LayerNameIsOptionalAndLeavesTheRunnerToUseTheCurrentLayer()
    {
      var part = SinglePart(Dimensions);
      Assert.Null(part.LayerName);
      Assert.Null(part.LayerColor);
    }

    [Fact]
    public void EmptyInputProducesAnEmptyDocument()
    {
      var doc = ListParser.Parse("");
      Assert.Empty(doc.Elements);
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void NullInputIsSafe()
      => Assert.Empty(ListParser.Parse(null).Elements);

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void HandlesEveryLineEndingStyle(string newline)
    {
      var text = string.Join(newline, "# Element: Boolean", "", "## Part", "", "* height: 1", "* width: 2", "* depth: 3");
      var doc = ListParser.Parse(text);
      Assert.False(doc.HasErrors);
      Assert.Single(doc.AllParts);
    }

    // ---- Element families -------------------------------------------------------

    [Theory]
    [InlineData("Boolean", ElementFamily.Solid)]
    [InlineData("BooleanDifference", ElementFamily.Solid)]
    [InlineData("Difference", ElementFamily.Solid)]
    [InlineData("BooleanUnion", ElementFamily.Union)]
    [InlineData("Union", ElementFamily.Union)]
    [InlineData("Loft", ElementFamily.Surface)]
    [InlineData("Revolve", ElementFamily.Surface)]
    [InlineData("Sweep1", ElementFamily.Surface)]
    [InlineData("Sweep2", ElementFamily.Surface)]
    [InlineData("Patch", ElementFamily.Surface)]
    [InlineData("Cap", ElementFamily.Surface)]
    [InlineData("boolean-difference", ElementFamily.Solid)]
    [InlineData("Sculpt", ElementFamily.Unknown)]
    public void MapsElementTypesToFamilies(string type, ElementFamily expected)
      => Assert.Equal(expected, ElementTypes.FamilyOf(type));

    [Fact]
    public void AnUnknownElementTypeWarns()
    {
      var doc = ListParser.Parse("# Element: Sculpt\n\n## Part\n\n" + Dimensions);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("Unknown element type"));
    }

    [Fact]
    public void DiagnosticsCarryTheSourceLineNumber()
    {
      var doc = ListParser.Parse("# Element: Boolean\n\n## Part\n\n* height: 1\n* width: 2\n");
      var error = Assert.Single(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error);
      Assert.Equal(3, error.Line);
      Assert.Contains("line 3", error.ToString());
    }
  }
}
