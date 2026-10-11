using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>The filletEdge block: loop selection, radius lists and face groups.</summary>
  [Trait("suite", "unit")]
  public class FilletEdgeTests
  {
    const string Box = "- height: 1\n- width: 2\n- depth: 3\n";

    static ListDocument Parse(string body) => ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);

    static FilletSpec Fillet(string block)
    {
      var part = Assert.Single(Parse(Box + "- filletEdge:\n" + block).AllParts);
      Assert.NotNull(part.Fillet);
      return part.Fillet;
    }

    static double[][] Radii(FilletSelection selection)
      => selection.Radii.Select(entry => entry.Select(m => m.Value).ToArray()).ToArray();

    /// <summary>Indents every line of a block by one level, so it sits under filletEdge.</summary>
    static string Nested(string block)
      => string.Concat(block.Split('\n').Where(l => l.Length > 0).Select(l => "  " + l + "\n"));

    /// <summary>A comparable summary of a part's fillet selections.</summary>
    static string Describe(Part part)
      => part.Fillet == null ? "(none)" : string.Join(" | ", part.Fillet.Selections.Select(s =>
           s.Axis + ":" + string.Join(",", s.Loops) + ":" +
           string.Join(";", s.Radii.Select(e => string.Join(",", e.Select(m => m.ToString()))))));

    // ---- Forms ----------------------------------------------------------------------

    [Fact]
    public void OneLoopWithOneRadius()
    {
      var selection = Assert.Single(Fillet("  - select: 1\n  - radius: .02\n").Selections);
      Assert.Equal(FilletAxis.Extrusion, selection.Axis);
      Assert.Equal(new[] { 1 }, selection.Loops);
      Assert.Equal(new[] { new[] { 0.02 } }, Radii(selection));
    }

    [Fact]
    public void BothLoopsWithARadiusEach()
    {
      var selection = Assert.Single(Fillet("  - select: 0,1\n  - radius: .1, .02\n").Selections);
      Assert.Equal(new[] { 0, 1 }, selection.Loops);
      Assert.Equal(new[] { new[] { 0.1 }, new[] { 0.02 } }, Radii(selection));
    }

    [Fact]
    public void BothLoopsWithOneSharedRadius()
    {
      // Fewer entries than loops: the runner repeats the last one.
      var selection = Assert.Single(Fillet("  - select: 0,1\n  - radius: .02\n").Selections);
      Assert.Equal(new[] { 0, 1 }, selection.Loops);
      Assert.Equal(new[] { new[] { 0.02 } }, Radii(selection));
    }

    [Fact]
    public void ABracketedListGoesEdgeByEdge()
    {
      var selection = Assert.Single(Fillet("  - select: 1\n  - radius: [.05,.05,.1,.02]\n").Selections);
      Assert.Equal(new[] { new[] { 0.05, 0.05, 0.1, 0.02 } }, Radii(selection));
    }

    [Fact]
    public void EachLoopCanHaveItsOwnEdgeList()
    {
      var selection = Assert.Single(Fillet("  - select: 0,1\n  - radius: [.1, .2], [.3, .15, .07, .03]\n").Selections);
      Assert.Equal(new[] { new[] { 0.1, 0.2 }, new[] { 0.3, 0.15, 0.07, 0.03 } }, Radii(selection));
    }

    [Fact]
    public void FaceGroupsCountLoopsAlongAWorldAxis()
    {
      var spec = Fillet(
        "  - select: top\n    - select: 0,1\n    - radius: .1, .2\n" +
        "  - select: front\n    - select: 0,1\n    - radius: [.01, .05], [.1]\n");

      Assert.Equal(2, spec.Selections.Count);
      Assert.Equal(FilletAxis.Z, spec.Selections[0].Axis);
      Assert.Equal(new[] { new[] { 0.1 }, new[] { 0.2 } }, Radii(spec.Selections[0]));
      Assert.Equal(FilletAxis.Y, spec.Selections[1].Axis);
      Assert.Equal(new[] { new[] { 0.01, 0.05 }, new[] { 0.1 } }, Radii(spec.Selections[1]));
    }

    [Theory]
    [InlineData("bottom", FilletAxis.Z)]
    [InlineData("back", FilletAxis.Y)]
    [InlineData("left", FilletAxis.X)]
    [InlineData("side", FilletAxis.X)]
    public void OppositeFaceWordsCountAlongTheSameAxis(string word, FilletAxis expected)
    {
      var doc = Parse(Box + "- filletEdge:\n  - select: " + word + "\n    - select: 0\n    - radius: .1\n");
      Assert.Equal(expected, Assert.Single(Assert.Single(doc.AllParts).Fillet.Selections).Axis);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Info && d.Message.Contains("lowest loop"));
    }

    [Theory]
    [InlineData("top", FilletAxis.Z)]
    [InlineData("front", FilletAxis.Y)]
    [InlineData("right", FilletAxis.X)]
    [InlineData("z", FilletAxis.Z)]
    public void FaceWordsNameTheirAxisWithoutComment(string word, FilletAxis expected)
    {
      var doc = Parse(Box + "- filletEdge:\n  - select: " + word + "\n    - select: 0\n    - radius: .1\n");
      Assert.Equal(expected, Assert.Single(Assert.Single(doc.AllParts).Fillet.Selections).Axis);
      Assert.Empty(doc.Diagnostics);
    }

    [Fact]
    public void ARadiusMayComeBeforeItsSelect()
    {
      var selection = Assert.Single(Fillet("  - radius: .02\n  - select: 1\n").Selections);
      Assert.Equal(new[] { 1 }, selection.Loops);
      Assert.Equal(new[] { new[] { 0.02 } }, Radii(selection));
    }

    [Fact]
    public void FilletRadiiAcceptArithmeticAndUnits()
    {
      var selection = Assert.Single(Fillet("  - select: 0,1\n  - radius: .04/2, 1mm\n").Selections);
      Assert.Equal(0.02, selection.Radii[0][0].Value, 9);
      Assert.Equal(LengthUnit.Millimeters, selection.Radii[1][0].Unit);
    }

    [Theory]
    [InlineData("filletEdge")]
    [InlineData("fillet")]
    [InlineData("fillet edges")]
    [InlineData("Fillet_Edge")]
    public void TheBlockKeyIgnoresCaseAndSeparators(string key)
      => Assert.NotNull(Assert.Single(Parse(Box + "- " + key + ":\n  - select: 1\n  - radius: .1\n").AllParts).Fillet);

    // ---- Problems -------------------------------------------------------------------

    [Theory]
    [InlineData("- select: 2\n- radius: .1\n", "is not 0")]
    [InlineData("- select: 1\n", "no radius")]
    [InlineData("- select: 1\n- radius: 0\n", "greater than zero")]
    [InlineData("- select: 1\n- radius: .1, .2\n", "extra entries")]
    [InlineData("- select: top\n", "needs nested")]
    [InlineData("- radius: .1\n", "no select")]
    [InlineData("- chamfer: .1\n", "Unknown filletEdge line")]
    [InlineData("- select: 1\n- radius: .1\n- radius: .2\n", "already has a radius")]
    public void ProblemsInsideTheBlockWarnAndThePartIsStillBuilt(string block, string message)
    {
      var doc = Parse(Box + "- filletEdge:\n" + Nested(block));
      Assert.False(doc.HasErrors);
      Assert.False(Assert.Single(doc.AllParts).HasError);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains(message));
    }

    [Fact]
    public void AFaceGroupInsideAFaceGroupIsIgnored()
    {
      var doc = Parse(Box + "- filletEdge:\n  - select: top\n    - select: front\n      - select: 0\n      - radius: .1\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("another face group"));
      Assert.Null(Assert.Single(doc.AllParts).Fillet);
    }

    [Fact]
    public void AValueOnTheFilletLineItselfIsIgnoredWithAWarning()
    {
      var doc = Parse(Box + "- filletEdge: .1\n  - select: 1\n  - radius: .1\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("'.1' is ignored"));
      Assert.NotNull(Assert.Single(doc.AllParts).Fillet);
    }

    [Fact]
    public void AnUnreadableFilletRadiusIsAnError()
    {
      var doc = Parse(Box + "- filletEdge:\n  - select: 1\n  - radius: big\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("Cannot read fillet radius"));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void AFilletOnAPartThatIsNotExtrudedWarns()
    {
      var doc = Parse("- height: 1\n- width: 1\n- filletEdge:\n  - select: 1\n  - radius: .1\n- actions: curve\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("extruded parts only"));
      Assert.False(doc.HasErrors);
    }

    // ---- Writing --------------------------------------------------------------------

    [Theory]
    [InlineData("  - select: 1\n  - radius: .02\n")]
    [InlineData("  - select: 0,1\n  - radius: .1, .02\n")]
    [InlineData("  - select: 0,1\n  - radius: .02\n")]
    [InlineData("  - select: 1\n  - radius: [.05,.05,.1,.02]\n")]
    [InlineData("  - select: 0,1\n  - radius: [.1, .2], [.3, .15, .07, .03]\n")]
    [InlineData("  - select: top\n    - select: 0,1\n    - radius: .1, .2\n  - select: front\n    - select: 0,1\n    - radius: [.01, .05], [.1]\n")]
    [InlineData("  - select: 1\n  - radius: .1\n  - select: right\n    - select: 0\n    - radius: .2\n  - select: 0\n  - radius: .3\n")]
    public void TheWriterRoundTripsEveryForm(string block)
    {
      var source = "# Element: Boolean\n\n## Part\n\n" + Box + "- filletEdge:\n" + block;
      var original = Assert.Single(ListParser.Parse(source).AllParts);
      var once = ListWriter.Write(ListParser.Parse(source));
      var reparsed = Assert.Single(ListParser.Parse(once).AllParts);

      Assert.Equal(Describe(original), Describe(reparsed));
      Assert.Equal(once, ListWriter.Write(ListParser.Parse(once)));
    }

    [Fact]
    public void TheWriterNestsTheBlockAndGroupsFaceSelections()
    {
      var written = ListWriter.Write(Parse(Box +
        "- filletEdge:\n  - select: top\n    - select: 0\n    - radius: .1\n    - select: 1\n    - radius: [.1, .2]\n")).Replace("\r\n", "\n");

      Assert.Contains(
        "- filletEdge:\n" +
        "  - select: top\n" +
        "    - select: 0\n" +
        "    - radius: 0.1\n" +
        "    - select: 1\n" +
        "    - radius: [0.1, 0.2]\n", written);
    }
  }
}
