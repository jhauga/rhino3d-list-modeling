using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Repeated headers and group references: continuing, updating and rebuilding earlier definitions.</summary>
  [Trait("suite", "unit")]
  public class HeaderReuseTests
  {
    const string Dimensions = "- height: 1\n- width: 1\n- depth: 1\n";

    /// <summary>A container "Pins" with three grouped parts at x 1, 2 and 3.</summary>
    const string ThreePins =
      "# Element: Boolean\n\n## Pins\n\n" + Dimensions + "\n" +
      "### group:p\n\n- position: 1, 0\n\n" +
      "### group:p\n\n- position: 2, 0\n\n" +
      "### group:p\n\n- position: 3, 0\n\n";

    static Part[] PartsOf(ListDocument doc, int element) => doc.Elements[element].Parts.ToArray();

    static double[] XOf(ListDocument doc, int element) => PartsOf(doc, element).Select(p => p.PositionA.Value).ToArray();

    // ---- Rebuilding ---------------------------------------------------------------------

    [Fact]
    public void ABareRepeatedPartIsRebuiltWithTheNewElementsDefaults()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Peg\n\n- shape: circle\n- radius: .25\n- depth: 2\n- position: 10, 0\n\n" +
        "# Element: Boolean\n\n- LayerName: Second\n- offset: 10\n\n## Peg\n");

      Assert.Empty(doc.Diagnostics);
      var first = Assert.Single(PartsOf(doc, 0));
      var again = Assert.Single(PartsOf(doc, 1));
      Assert.Null(first.LayerName);
      Assert.Equal("Peg", again.Name);
      Assert.Equal("Second", again.LayerName);
      Assert.Equal(10.0, again.Offset.Value, 9);
      Assert.Equal(0.25, again.Width.Value.Value, 9);
      Assert.Equal(10.0, again.PositionA.Value, 9);
      Assert.Equal(15, again.Line);
    }

    [Fact]
    public void ABareRepeatedContainerRebuildsAllItsPartsInOrder()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Pins\n\n" + Dimensions + "\n" +
        "### group:p\n\n- position: 1, 0\n\n" +
        "### group:p\n\n- position: 2, 0\n\n" +
        "### Spare\n\n- position: 3, 0\n\n" +
        "# Element: Boolean\n\n## Pins\n");

      Assert.False(doc.HasErrors);
      var parts = PartsOf(doc, 1);
      Assert.Equal(new[] { 1.0, 2.0, 3.0 }, XOf(doc, 1));
      Assert.Equal(0, parts[0].GroupIndex);
      Assert.Equal(1, parts[1].GroupIndex);
      Assert.Null(parts[2].GroupIndex);
      Assert.Equal("Pins [group:p][1]", parts[1].DisplayName);
      Assert.Equal("Spare", parts[2].DisplayName);
      // Parts built where they are written keep the plain form.
      Assert.Equal("Pins [group:p]", PartsOf(doc, 0)[0].DisplayName);
    }

    [Fact]
    public void ARepeatedPartNameWithLinesIsANewPart()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Hole\n\n" + Dimensions + "- position: 1, 0\n\n## Hole\n\n" + Dimensions + "- position: 2, 0\n");

      Assert.Empty(doc.Diagnostics);
      Assert.Equal(new[] { 1.0, 2.0 }, XOf(doc, 0));
    }

    [Fact]
    public void ASecondMentionInOneElementIsSkippedWithAnInfoLine()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Peg\n\n" + Dimensions + "\n# Element: Boolean\n\n## Peg\n\n## Peg\n");

      Assert.Single(PartsOf(doc, 1));
      var info = Assert.Single(doc.Diagnostics);
      Assert.Equal(DiagnosticLevel.Info, info.Level);
      Assert.Contains("already built in this element (line 11)", info.Message);
      Assert.Equal(13, info.Line);
    }

    [Fact]
    public void AContainerStillOpenIsNotContinuedByAHeaderUnderIt()
    {
      var part = Assert.Single(ListParser.Parse("# Element: Boolean\n\n## A\n\n- LayerName: L\n\n### A\n\n" + Dimensions).AllParts);
      Assert.Equal("A", part.Name);
      Assert.Equal("L", part.LayerName);
    }

    // ---- Continuing and updating ------------------------------------------------------

    [Fact]
    public void AnUpdateBuildsNothingAndChangesOnlyLaterRebuilds()
    {
      var doc = ListParser.Parse(
        ThreePins +
        "# Element: Boolean\n\n## Pins\n\n- depth: 5\n\n" +
        "# Element: Boolean\n\n## Pins\n");

      Assert.All(PartsOf(doc, 0), p => Assert.Equal(1.0, p.Depth.Value.Value, 9));
      Assert.Empty(PartsOf(doc, 1));
      Assert.All(PartsOf(doc, 2), p => Assert.Equal(5.0, p.Depth.Value.Value, 9));
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Info && d.Line == 23 &&
        d.Message.Contains("continues the container from line 3") && d.Message.Contains("nothing is built from it here"));
    }

    [Fact]
    public void AnUpdateWrittenAfterAReferenceDoesNotChangeIt()
    {
      var doc = ListParser.Parse(
        ThreePins +
        "# Element: Boolean\n\n## Again\n\n### group:p[0]\n\n## Pins\n\n- depth: 5\n");

      Assert.Equal(1.0, Assert.Single(PartsOf(doc, 1)).Depth.Value.Value, 9);
    }

    [Fact]
    public void AContinuationWithHeadersUnderItBuildsOnlyThose()
    {
      var doc = ListParser.Parse(ThreePins + "# Element: Boolean\n\n## Pins\n\n### group:p[1]\n");
      Assert.Equal(new[] { 2.0 }, XOf(doc, 1));
    }

    [Fact]
    public void ANewHeaderUnderAContinuationStartsFromTheContainer()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Pins\n\n- LayerName: Pins\n" + Dimensions + "\n### group:p\n\n- position: 1, 0\n\n" +
        "# Element: Boolean\n\n## Pins\n\n### group:p\n\n- position: 9, 0\n");

      var part = Assert.Single(PartsOf(doc, 1));
      Assert.Equal("Pins", part.LayerName);
      Assert.Equal(9.0, part.PositionA.Value, 9);
      Assert.Null(part.GroupIndex);
    }

    // ---- Group references ---------------------------------------------------------------

    [Fact]
    public void AStarReferenceRebuildsEveryPartWrittenBeforeIt()
    {
      var doc = ListParser.Parse(
        ThreePins +
        "# Element: Boolean\n\n## Again\n\n### group:p[*]\n\n" +
        "# Element: Boolean\n\n## Pins\n\n### group:p\n\n- position: 4, 0\n");

      Assert.Equal(new[] { 1.0, 2.0, 3.0 }, XOf(doc, 1));
      Assert.Equal(new int?[] { 0, 1, 2 }, PartsOf(doc, 1).Select(p => p.GroupIndex).ToArray());
      Assert.All(PartsOf(doc, 1), p => Assert.Equal("p", p.Group));
    }

    [Fact]
    public void ANumberedReferenceCountsFromZero()
    {
      var doc = ListParser.Parse(ThreePins + "# Element: Boolean\n\n## Again\n\n### group:p[0]\n\n### group:p[2]\n");
      Assert.Equal(new[] { 1.0, 3.0 }, XOf(doc, 1));
    }

    [Theory]
    [InlineData("group:nowhere[0]", "No group 'nowhere' is written before line 25.")]
    [InlineData("group:p[3]", "Group 'p' has 3 parts before line 25, counting from 0; [3] is not one of them.")]
    [InlineData("group:p[1-3]", "Cannot read '[1-3]' after group 'p'; write [*] or one number, counting from 0.")]
    public void AReferenceThatCannotBeFollowedIsAnErrorForThatReferenceOnly(string header, string message)
    {
      var doc = ListParser.Parse(ThreePins + "# Element: Boolean\n\n## Again\n\n### " + header + "\n\n### group:p[0]\n");

      var error = Assert.Single(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error);
      Assert.Equal(message, error.Message);
      Assert.Equal(25, error.Line);
      Assert.Equal(new[] { 1.0 }, XOf(doc, 1));
    }

    [Fact]
    public void SiteLinesOverrideAndAnEmptyOneFallsBackToTheContext()
    {
      var part = Assert.Single(PartsOf(ListParser.Parse(
        "# Element: Boolean\n\n## Holes\n\n- LayerName: Holes\n" + Dimensions + "\n### group:h\n\n- position: 1, 0\n\n" +
        "# Element: Boolean\n\n- LayerName: Shop\n\n## Again\n\n### group:h[0]\n\n- depth: 7\n- LayerName:\n"), 1));

      Assert.Equal(7.0, part.Depth.Value.Value, 9);
      Assert.Equal("Shop", part.LayerName);
    }

    [Fact]
    public void AHeaderUnderAReferenceWarnsAndIsReadBesideIt()
    {
      var doc = ListParser.Parse(
        ThreePins + "# Element: Boolean\n\n## Again\n\n- LayerName: Again\n\n### group:p[0]\n\n#### Extra\n\n" + Dimensions);

      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("cannot hold headers"));
      var parts = PartsOf(doc, 1);
      Assert.Equal(2, parts.Length);
      Assert.Equal("Extra", parts[1].Name);
      Assert.Equal("Again", parts[1].LayerName);
    }

    // ---- Where a rebuilt part's values come from ----------------------------------------

    const string PadCutters =
      "# Element: Boolean\n\n## Cuts\n\n- actions: subtract\n" + Dimensions + "\n### group:pads\n\n- position: 1, 0\n\n";

    [Fact]
    public void TheChainOutranksTheHeadersAroundTheRebuild()
    {
      var doc = ListParser.Parse(PadCutters + "# Element: Boolean\n\n## Merge\n\n- actions: union\n\n### group:pads[0]\n");
      Assert.Equal(PartRole.Subtract, Assert.Single(PartsOf(doc, 1)).Role);
    }

    [Fact]
    public void AnEmptyValueInTheChainLetsTheHeadersAroundTheRebuildThrough()
    {
      var doc = ListParser.Parse(
        PadCutters + "# Element: Boolean\n\n## Cuts\n\n- actions:\n\n## Merge\n\n- actions: union\n\n### group:pads[0]\n");

      Assert.Equal(PartRole.Union, Assert.Single(PartsOf(doc, 1)).Role);
      Assert.Equal(PartRole.Subtract, Assert.Single(PartsOf(doc, 0)).Role);
    }

    [Fact]
    public void AnExplicitReferenceDecidesWhichPartsABareRebuildBuilds()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Board\n\n### Pads\n\n" + Dimensions + "\n" +
        "#### group:p\n\n- position: 1, 0\n\n#### group:p\n\n- position: 2, 0\n\n#### group:p\n\n- position: 3, 0\n\n" +
        "# Element: Boolean\n\n## Again\n\n### Pads\n\n### Holder\n\n#### group:p[0]\n\n#### group:p[2]\n");

      Assert.Equal(new[] { 1.0, 3.0 }, XOf(doc, 1));
      Assert.Empty(doc.Diagnostics);
    }

    /// <summary>
    /// The circuit board case with plain rectangles, since its square, z-position and
    /// through values come later: the second element clears the cutters' action, moves
    /// them to a layer, and builds them again as solids, with the odd nesting of the case.
    /// </summary>
    [Fact]
    public void TheCaseStructureBuildsThePadsAgainAsSolidsAndTheHolesAsCutters()
    {
      var doc = ListParser.Parse(
        "# Element: BooleanDifference\n\n" +
        "## Pin Connections\n\n- actions: subtract\n\n" +
        "### Pin Top Connection\n\n- height: .1\n- width: .1\n- depth: .004\n- offset: .046\n\n" +
        "#### group:top\n\n- position: -.8, .35\n\n#### group:top\n\n- position: .8, .35\n\n" +
        "### Pin Bottom Connection\n\n- height: .1\n- width: .1\n- depth: .004\n- offset: -.05\n\n" +
        "#### group:bottom\n\n- position: -.8, .35\n\n#### group:bottom\n\n- position: .8, .35\n\n" +
        "### Pin Holes\n\n- radius: .015\n- depth: .2\n- offset: -.1\n\n" +
        "#### group:holes\n\n- position: -.8, .35\n\n#### group:holes\n\n- position: .8, .35\n\n" +
        "# Element: BooleanDifference\n\n" +
        "## Pin Connections\n\n- actions:\n- LayerName: Pads\n\n" +
        "## Pin Circuits\n\n- actions: extrude\n\n" +
        "### Pin Top Connection\n\n### Pin Bottom Connection\n\n" +
        "#### group:top[*]\n\n#### group:bottom[*]\n\n" +
        "## Pin Through Holes\n\n- actions: subtract\n\n" +
        "### Pin Holes\n\n#### group:holes[*]\n");

      var info = Assert.Single(doc.Diagnostics);
      Assert.Contains("continues the container", info.Message);

      Assert.All(PartsOf(doc, 0), p => Assert.Equal(PartRole.Subtract, p.Role));
      Assert.All(PartsOf(doc, 0), p => Assert.Null(p.LayerName));

      var built = PartsOf(doc, 1);
      Assert.Equal(new[] { "Pin Top Connection", "Pin Top Connection", "Pin Bottom Connection", "Pin Bottom Connection", "Pin Holes", "Pin Holes" },
        built.Select(p => p.Name).ToArray());
      Assert.All(built, p => Assert.Equal("Pads", p.LayerName));
      Assert.All(built.Take(4), p => Assert.Equal(PartRole.Keep, p.Role));
      Assert.All(built.Skip(4), p => Assert.Equal(PartRole.Subtract, p.Role));
      // The top pads sit under Pin Bottom Connection here, but keep their own offset.
      Assert.Equal(new[] { 0.046, 0.046, -0.05, -0.05, -0.1, -0.1 }, built.Select(p => p.Offset.Value).ToArray());
      Assert.Equal(PartShape.Circle, built[4].Shape);
    }
  }
}
