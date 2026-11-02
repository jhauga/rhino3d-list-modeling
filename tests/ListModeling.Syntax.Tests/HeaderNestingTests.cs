using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Containers at any depth, element defaults, and empty values that clear what a header above set.</summary>
  [Trait("suite", "unit")]
  public class HeaderNestingTests
  {
    const string Dimensions = "- height: 1\n- width: 2\n- depth: 3\n";

    static Part SinglePart(string body) => Assert.Single(ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body).AllParts);

    // ---- Containers at any depth ------------------------------------------------------

    [Fact]
    public void AGroupUnderALevelThreeContainerInheritsFromEveryHeaderAbove()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n" +
        "## Fasteners\n\n- actions: subtract\n\n" +
        "### Screw\n\n- shape: circle\n- radius: .1\n- depth: 2\n\n" +
        "#### group:screws\n\n- position: 1, 1\n\n" +
        "#### group:screws\n\n- position: 3, 1\n");

      Assert.False(doc.HasErrors);
      var parts = doc.AllParts.ToList();
      // Fasteners and Screw both have deeper headers, so neither is a part of its own.
      Assert.Equal(2, parts.Count);
      Assert.All(parts, p =>
      {
        Assert.Equal("Screw", p.Name);
        Assert.Equal("screws", p.Group);
        Assert.Equal(PartRole.Subtract, p.Role);
        Assert.Equal(PartShape.Circle, p.Shape);
        Assert.Equal(0.1, p.Width.Value.Value, 9);
        Assert.Equal(2.0, p.Depth.Value.Value, 9);
      });
      Assert.Equal(1.0, parts[0].PositionA.Value, 9);
      Assert.Equal(3.0, parts[1].PositionA.Value, 9);
    }

    [Fact]
    public void TheNearestHeaderWinsAcrossThreeLevels()
    {
      var part = Assert.Single(ListParser.Parse(
        "# Element: Boolean\n\n" +
        "## Outer\n\n- LayerName: Outer\n- height: 1\n- width: 1\n- depth: 1\n\n" +
        "### Middle\n\n- LayerName: Middle\n- depth: 2\n\n" +
        "#### Inner\n\n- depth: 3\n").AllParts);

      Assert.Equal("Inner", part.Name);
      Assert.Equal("Middle", part.LayerName);
      Assert.Equal(3.0, part.Depth.Value.Value, 9);
      Assert.Equal(1.0, part.Height.Value.Value, 9);
    }

    [Fact]
    public void ALaterLineUnderOneHeaderWins()
      => Assert.Equal(5.0, SinglePart(Dimensions + "- depth: 5\n").Depth.Value.Value, 9);

    [Fact]
    public void AHeaderMaySkipALevel()
    {
      var part = Assert.Single(ListParser.Parse(
        "# Element: Boolean\n\n## Holes\n\n" + Dimensions + "\n#### group:h\n\n- position: 1, 1\n").AllParts);

      Assert.Equal("Holes", part.Name);
      Assert.Equal("h", part.Group);
      Assert.Equal(1.0, part.Height.Value.Value, 9);
      Assert.Equal(1.0, part.PositionA.Value, 9);
    }

    [Fact]
    public void AShallowerHeaderEndsTheContainersBelowIt()
    {
      var parts = ListParser.Parse(
        "# Element: Boolean\n\n" +
        "## A\n\n- LayerName: A\n\n" +
        "### B\n\n- LayerName: B\n\n" +
        "#### group:x\n\n" + Dimensions + "\n" +
        "### C\n\n" + Dimensions).AllParts.ToList();

      Assert.Equal(2, parts.Count);
      Assert.Equal("B", parts[0].Name);
      Assert.Equal("B", parts[0].LayerName);
      Assert.Equal("C", parts[1].Name);
      Assert.Equal("A", parts[1].LayerName);
    }

    [Fact]
    public void AGroupedPartIsNamedAfterTheNearestHeaderThatIsNotAGroup()
    {
      var part = Assert.Single(ListParser.Parse(
        "# Element: Boolean\n\n## Board\n\n### Pads\n\n" + Dimensions + "\n#### group:pads\n").AllParts);

      Assert.Equal("Pads", part.Name);
      Assert.Equal("Pads [group:pads]", part.DisplayName);
    }

    // ---- Element defaults -------------------------------------------------------------

    [Fact]
    public void ElementDefaultsApplyToEveryPartBeneathWhatItsHeadersSay()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n- LayerName: Shop\n- depth: 9\n\n" +
        "## Plate\n\n- height: 1\n- width: 1\n\n" +
        "## Shim\n\n- height: 1\n- width: 1\n- depth: 2\n");

      Assert.Empty(doc.Diagnostics);
      var parts = doc.AllParts.ToList();
      Assert.All(parts, p => Assert.Equal("Shop", p.LayerName));
      Assert.Equal(9.0, parts[0].Depth.Value.Value, 9);
      Assert.Equal(2.0, parts[1].Depth.Value.Value, 9);
    }

    [Fact]
    public void ElementDefaultsStayInTheirElement()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n- LayerName: Shop\n\n## A\n\n" + Dimensions +
        "\n# Element: Boolean\n\n## B\n\n" + Dimensions);

      Assert.Equal("Shop", doc.Elements[0].Parts.Single().LayerName);
      Assert.Null(doc.Elements[1].Parts.Single().LayerName);
    }

    // ---- Empty values -------------------------------------------------------------------

    [Fact]
    public void AnEmptyValueClearsWhatAContainerSetWithoutADiagnostic()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n" +
        "## Holes\n\n- LayerName: Holes\n- orientation: front\n- shape: hexagon\n- position: 4, 5\n- offset: 6\n- actions: subtract\n" +
        Dimensions + "\n" +
        "### Plain\n\n- LayerName:\n- orientation:\n- shape:\n- position:\n- offset: none\n- actions: null\n");

      Assert.Empty(doc.Diagnostics);
      var part = Assert.Single(doc.AllParts);
      Assert.Null(part.LayerName);
      Assert.Equal(Orientation.Top, part.Orientation);
      Assert.Equal(PartShape.Rectangle, part.Shape);
      Assert.Equal(0.0, part.PositionA.Value, 9);
      Assert.Equal(0.0, part.PositionB.Value, 9);
      Assert.Equal(0.0, part.Offset.Value, 9);
      Assert.Equal(PartGenerator.Extrude, part.Generator);
      Assert.Equal(PartRole.Keep, part.Role);
    }

    [Fact]
    public void AnEmptyValueFallsBackToTheElementDefault()
    {
      var part = Assert.Single(ListParser.Parse(
        "# Element: Boolean\n\n- LayerName: Shop\n\n" +
        "## Holes\n\n- LayerName: Holes\n" + Dimensions + "\n" +
        "### Plain\n\n- LayerName:\n").AllParts);

      Assert.Equal("Shop", part.LayerName);
    }

    [Fact]
    public void AnEmptyValueOnItsOwnPartStillReadsAsLeftOut()
    {
      var part = SinglePart(Dimensions + "- orientation:\n- shape:\n- position:\n");
      Assert.Equal(Orientation.Top, part.Orientation);
      Assert.Equal(PartShape.Rectangle, part.Shape);
      Assert.False(part.HasError);
    }

    // ---- Diagnostics through containers -------------------------------------------------

    [Fact]
    public void AContainerValueThatCannotBeReadIsReportedOnceAndMarksEveryPart()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Pins\n\n- height: tall\n- width: 1\n- depth: 1\n\n" +
        "### group:p\n\n- position: 1, 1\n\n" +
        "### group:p\n\n- position: 2, 2\n");

      var unreadable = Assert.Single(doc.Diagnostics, d => d.Message.Contains("Cannot read height"));
      Assert.Equal(5, unreadable.Line);
      Assert.Equal(2, doc.AllParts.Count());
      Assert.All(doc.AllParts, p => Assert.True(p.HasError));
    }

    [Fact]
    public void AnUnknownKeyOnAContainerIsKeptOnEveryPart()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Pins\n\n- material: brass\n" + Dimensions + "\n### group:p\n\n### group:p\n");

      Assert.Equal(2, doc.AllParts.Count());
      Assert.All(doc.AllParts, p => Assert.Equal("brass", p.Extra["material"]));
      Assert.Single(doc.Diagnostics, d => d.Message.Contains("material"));
    }
  }
}
