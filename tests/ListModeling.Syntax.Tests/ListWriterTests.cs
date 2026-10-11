using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>
  /// The writer produces the form the parser reads, so a list generated from a selection
  /// or built step by step can be edited and rerun. Every test here asserts against a
  /// reparse rather than against exact text, except where the wording is the point.
  /// </summary>
  [Trait("suite", "unit")]
  public class ListWriterTests
  {
    static string WriteBack(string source) => ListWriter.Write(ListParser.Parse(source));

    const string Dimensions = "* height: 1\n* width: 2\n* depth: 3\n";

    [Fact]
    public void WritesHeadersTheParserReadsBack()
    {
      var written = WriteBack("# Element: BooleanDifference\n\n## Circuit Board\n\n" + Dimensions);
      Assert.Contains("# Element: BooleanDifference", written);
      Assert.Contains("## Circuit Board", written);

      var doc = ListParser.Parse(written);
      Assert.False(doc.HasErrors);
      Assert.Equal("Circuit Board", Assert.Single(doc.AllParts).Name);
    }

    [Fact]
    public void WritesTheContainerHeaderOnceAboveItsGroupedParts()
    {
      var written = WriteBack(
        "# Element: BooleanDifference\n\n## Mount Holes\n\n" +
        "### group:holes\n\n- radius: 1\n- depth: 2\n- position: 1,1\n- actions: subtract\n\n" +
        "### group:holes\n\n- radius: 1\n- depth: 2\n- position: -1,-1\n- actions: subtract\n");

      Assert.Equal(1, written.Split(new[] { "## Mount Holes" }, System.StringSplitOptions.None).Length - 1);
      Assert.Equal(2, written.Split(new[] { "### group:holes" }, System.StringSplitOptions.None).Length - 1);

      var parts = ListParser.Parse(written).AllParts.ToList();
      Assert.Equal(2, parts.Count);
      Assert.All(parts, p => Assert.Equal("holes", p.Group));
    }

    [Fact]
    public void WritesASingleRadiusForCircles()
    {
      var written = WriteBack("# Element: Boolean\n\n## Hole\n\n* shape: circle\n* radius: .05in\n* depth: 1in\n");
      Assert.Contains("- shape: circle", written);
      Assert.Contains("- radius: 0.05in", written);
      Assert.DoesNotContain("- width:", written);
      Assert.DoesNotContain("- height:", written);
    }

    [Fact]
    public void WritesASingleRadiusForPolygons()
    {
      var written = WriteBack("# Element: Boolean\n\n## Nut\n\n* shape: hexagon\n* radius: 4\n* depth: 2\n");
      Assert.Contains("- shape: polygon:6", written);
      Assert.Contains("- radius: 4", written);
    }

    [Fact]
    public void WritesBothRadiiForEllipses()
    {
      var written = WriteBack("# Element: Boolean\n\n## Oval\n\n* shape: ellipse\n* height: 3\n* width: 5\n* depth: 1\n");
      Assert.Contains("- shape: ellipse", written);
      Assert.Contains("- height: 3", written);
      Assert.Contains("- width:  5", written);
      Assert.DoesNotContain("- radius:", written);
    }

    [Fact]
    public void OmitsTheShapeLineForTheRectangleDefault()
      => Assert.DoesNotContain("- shape:", WriteBack("# Element: Boolean\n\n## Plate\n\n" + Dimensions));

    [Fact]
    public void OmitsAZeroOffset()
      => Assert.DoesNotContain("- offset:", WriteBack("# Element: Boolean\n\n## Plate\n\n" + Dimensions));

    [Fact]
    public void WritesANonZeroOffset()
      => Assert.Contains("- offset: 12mm", WriteBack("# Element: Boolean\n\n## Plate\n\n" + Dimensions + "* offset: 12mm\n"));

    [Fact]
    public void OmitsAnAbsentLayer()
    {
      var written = WriteBack("# Element: Boolean\n\n## Plate\n\n" + Dimensions);
      Assert.DoesNotContain("LayerName", written);
      Assert.DoesNotContain("LayerColor", written);
    }

    [Fact]
    public void KeepsUnitsAsTheyWereWritten()
    {
      var written = WriteBack("# Element: Boolean\n\n## Plate\n\n* height: .84in\n* width: 10mm\n* depth: 3\n");
      Assert.Contains("- height: 0.84in", written);
      Assert.Contains("- width:  10mm", written);
      Assert.Contains("- depth:  3", written);
    }

    [Fact]
    public void CarriesUnknownPropertiesThrough()
      => Assert.Contains("- material: aluminium", WriteBack("# Element: Boolean\n\n## Plate\n\n" + Dimensions + "* material: aluminium\n"));

    [Fact]
    public void WritingIsIdempotent()
    {
      var once = WriteBack(
        "# Element: BooleanDifference\n\n## Board\n\n* LayerName: Deck\n* LayerColor: Dark Blue\n" +
        "* orientation: front\n* height: .84in\n* width: 1.68in\n* depth: .04in\n* position: 1, 2\n* actions: extrude\n\n" +
        "## Holes\n\n### group:h\n\n- shape: circle\n- radius: .05in\n- depth: 1in\n- position: .62, -.35\n- actions: subtract\n");

      Assert.Equal(once, WriteBack(once));
    }

    [Fact]
    public void WritesHyphenBulletsWhateverTheSourceUsed()
    {
      var written = WriteBack("# Element: Boolean\n\n## Plate\n\n* LayerName: Deck\n+ height: 1\n* width: 2\n- depth: 3\n");
      var bullets = written.Replace("\r\n", "\n").Split('\n').Where(l => l.TrimStart().Length > 0 && !l.StartsWith("#")).ToList();
      Assert.NotEmpty(bullets);
      Assert.All(bullets, l => Assert.StartsWith("- ", l));
    }

    [Fact]
    public void AnEmptyDocumentWritesNothing()
      => Assert.Equal(string.Empty, ListWriter.Write(new ListDocument()));
  }
}
