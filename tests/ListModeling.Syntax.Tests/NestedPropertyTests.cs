using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Indented bullets, quiet comment lines, and nested layer paths.</summary>
  [Trait("suite", "unit")]
  public class NestedPropertyTests
  {
    const string Dimensions = "- height: 1\n- width: 2\n- depth: 3\n";

    static ListDocument Parse(string body) => ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);

    static Part SinglePart(string body) => Assert.Single(Parse(body).AllParts);

    static string Normalize(string text) => text.Replace("\r\n", "\n");

    // ---- Blocks ---------------------------------------------------------------------

    [Fact]
    public void ARadiusNestedUnderFilletEdgeLeavesThePartRadiusAlone()
    {
      var part = SinglePart("- radius: .13\n- depth: 1\n- filletEdge:\n  - select: 1\n  - radius: .02\n");
      Assert.Equal(0.13, part.Width.Value.Value, 9);
      Assert.Equal(0.13, part.Height.Value.Value, 9);
      Assert.Equal(0.02, part.Fillet.Selections[0].Radii[0][0].Value, 9);
    }

    [Fact]
    public void ABlockCanSitBeforeThePropertiesAfterIt()
    {
      var part = SinglePart("- shape: circle\n- filletEdge:\n  - select: 0,1\n  - radius: .02\n- radius: 0.13\n- depth: 0.39\n");
      Assert.Equal(0.13, part.Width.Value.Value, 9);
      Assert.Equal(new[] { 0, 1 }, part.Fillet.Selections[0].Loops);
      Assert.False(part.HasError);
    }

    [Fact]
    public void LinesNestedUnderASingleValuePropertyReadAsOrdinaryProperties()
    {
      // An unevenly indented list reads the same as a flat one.
      var doc = Parse("- orientation: front\n  - height: 1\n  - width: 2\n  - depth: 3\n");
      Assert.False(doc.HasErrors);
      var part = Assert.Single(doc.AllParts);
      Assert.Equal(Orientation.Front, part.Orientation);
      Assert.Equal(1.0, part.Height.Value.Value, 9);
      Assert.Equal(3.0, part.Depth.Value.Value, 9);
    }

    [Fact]
    public void LinesNestedUnderAnUnknownPropertyAreKeptFlattened()
    {
      var part = SinglePart(Dimensions + "- material:\n  - name: steel\n  - finish: matte\n");
      Assert.Equal("steel", part.Extra["material.name"]);
      Assert.Equal("matte", part.Extra["material.finish"]);
      Assert.False(part.Extra.ContainsKey("material"));
    }

    [Fact]
    public void FlattenedUnknownLinesRoundTrip()
    {
      var once = ListWriter.Write(Parse(Dimensions + "- material:\n  - name: steel\n"));
      Assert.Contains("- material.name: steel", once);
      Assert.Equal(once, ListWriter.Write(ListParser.Parse(once)));
    }

    [Fact]
    public void AnEmptyValueWithNothingNestedStillMeansLeftOut()
      => Assert.Null(SinglePart(Dimensions + "- LayerName:\n").LayerName);

    [Fact]
    public void ANumericKeyAtTheTopLevelIsIgnoredLikeProse()
    {
      var doc = Parse(Dimensions + "- 0: .125\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Info && d.Message.StartsWith("Ignored"));
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void DiagnosticsKeepTheOrderOfTheLinesTheyCameFrom()
    {
      var doc = Parse("- orientation: sideways\nSome prose.\n- shape: blob\n" + Dimensions);
      Assert.Equal(new[] { 5, 6, 7 }, doc.Diagnostics.Select(d => d.Line));
    }

    [Fact]
    public void GroupedPartsGetTheirOwnCopyOfTheContainerFillet()
    {
      var doc = ListParser.Parse(
        "# Element: Boolean\n\n## Posts\n\n" +
        "- radius: 1\n- depth: 2\n- filletEdge:\n  - select: 1\n  - radius: .1\n\n" +
        "### group:a\n\n- position: 1, 1\n\n### group:b\n\n- position: 2, 2\n");

      var parts = doc.AllParts.ToList();
      Assert.Equal(2, parts.Count);
      Assert.All(parts, p => Assert.Equal(new[] { 1 }, p.Fillet.Selections[0].Loops));
      Assert.NotSame(parts[0].Fillet, parts[1].Fillet);
      Assert.False(doc.HasErrors);
    }

    // ---- Quiet lines ----------------------------------------------------------------

    [Theory]
    [InlineData("> NOTE: a quoted note")]
    [InlineData(">")]
    [InlineData("---")]
    [InlineData("***")]
    [InlineData("___")]
    [InlineData("- - -")]
    public void BlockQuotesAndThematicBreaksAreSilent(string line)
      => Assert.Empty(Parse(line + "\n" + Dimensions).Diagnostics);

    [Fact]
    public void AnHtmlCommentMaySpanSeveralLines()
    {
      var doc = Parse("<!-- first line\n  second line\n  third line -->\n" + Dimensions);
      Assert.Empty(doc.Diagnostics);
      Assert.Equal(1.0, Assert.Single(doc.AllParts).Height.Value.Value, 9);
    }

    [Fact]
    public void TextAfterAMultiLineCommentClosesStillReads()
    {
      var doc = Parse("<!-- note\n -->- height: 1\n- width: 2\n- depth: 3\n");
      Assert.Empty(doc.Diagnostics);
      Assert.Equal(1.0, Assert.Single(doc.AllParts).Height.Value.Value, 9);
    }

    // ---- Nested layers --------------------------------------------------------------

    [Theory]
    [InlineData("- LayerName: Pipe::simple\n")]
    [InlineData("- LayerName: Pipe :: simple\n")]
    [InlineData("- LayerName: \"Pipe\", \"simple\"\n")]
    [InlineData("- LayerName: \"Pipe\",\"simple\"\n")]
    [InlineData("- LayerName: \"Pipe::simple\"\n")]
    [InlineData("- LayerName: Pipe\n  - simple\n")]
    [InlineData("- LayerName: Pipe\n  - \"simple\"\n")]
    [InlineData("- LayerName: Pipe\n    - simple\n")]
    public void EveryNestedLayerFormReadsAsOnePath(string line)
      => Assert.Equal("Pipe::simple", SinglePart(line + Dimensions).LayerName);

    [Fact]
    public void NestedLayerBulletsCanGoSeveralLevelsDeep()
      => Assert.Equal("Site::Block::Floor", SinglePart("- LayerName: Site\n  - Block\n    - Floor\n" + Dimensions).LayerName);

    [Fact]
    public void ACommaWithoutQuotesStaysPartOfAFlatName()
      => Assert.Equal("Bolts, spare", SinglePart("- LayerName: Bolts, spare\n" + Dimensions).LayerName);

    [Fact]
    public void TwoNestedLayersAtOneLevelWarnAndTheFirstWins()
    {
      var doc = Parse("- LayerName: Pipe\n  - simple\n  - multiple\n" + Dimensions);
      Assert.Equal("Pipe::simple", Assert.Single(doc.AllParts).LayerName);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("more than one nested layer"));
    }

    [Fact]
    public void AKeyedLineUnderLayerNameIsAnOrdinaryProperty()
    {
      var part = SinglePart("- LayerName: Pipe\n  - simple\n  - LayerColor: Black\n" + Dimensions);
      Assert.Equal("Pipe::simple", part.LayerName);
      Assert.Equal("Black", part.LayerColor);
    }

    [Fact]
    public void TheWriterUsesQuotedNamesForANestedLayer()
    {
      var written = ListWriter.Write(Parse("- LayerName: Pipe::simple\n" + Dimensions));
      Assert.Contains("- LayerName: \"Pipe\", \"simple\"", written);
      Assert.Equal("Pipe::simple", Assert.Single(ListParser.Parse(written).AllParts).LayerName);
    }

    [Fact]
    public void TheWriterLeavesAFlatLayerBare()
      => Assert.Contains("- LayerName: Deck\n", Normalize(ListWriter.Write(Parse("- LayerName: Deck\n" + Dimensions))));
  }
}
