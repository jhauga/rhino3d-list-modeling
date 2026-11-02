using System.IO;
using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Cutters with "depth: through", parts that select solids already in the model, and the case that uses both.</summary>
  [Trait("suite", "unit")]
  public class ThroughAndSelectTests
  {
    static ListDocument Parse(string body) => ListParser.Parse("# Element: BooleanDifference\n\n## Part\n\n" + body);

    static string Normalize(string text) => text.Replace("\r\n", "\n");

    // ---- Through ------------------------------------------------------------------------

    [Theory]
    [InlineData("through")]
    [InlineData("THROUGH")]
    public void ThroughOnASubtractPartTakesTheDepthFromTheSolidsItCuts(string word)
    {
      var doc = Parse("- shape: circle\n- radius: .2\n- depth: " + word + "\n- actions: subtract\n");
      Assert.Empty(doc.Diagnostics);
      var part = Assert.Single(doc.AllParts);
      Assert.True(part.DepthThrough);
      Assert.Null(part.Depth);
      Assert.False(part.HasError);
      Assert.Contains("- depth:  through\n", Normalize(ListWriter.Write(doc)));
    }

    [Theory]
    [InlineData("extrude")]
    [InlineData("union")]
    public void ThroughOnAPartThatDoesNotCutIsAnError(string action)
    {
      var doc = Parse("- radius: .2\n- depth: through\n- actions: " + action + "\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("'through' only applies to subtract parts"));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void AFilletOnAThroughCutterWarnsAndIsIgnored()
    {
      var doc = Parse("- radius: .2\n- depth: through\n- filletEdge:\n  - select: 1\n  - radius: .02\n- actions: subtract\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("does nothing on a through cutter"));
      Assert.False(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void ALaterDepthReplacesThroughAndAnEmptyOneClearsIt()
    {
      var doc = ListParser.Parse(
        "# Element: BooleanDifference\n\n## Holes\n\n- radius: .2\n- depth: through\n- actions: subtract\n\n" +
        "### group:h\n\n### group:h\n\n- depth: 2\n\n### group:h\n\n- depth:\n");

      var parts = doc.AllParts.ToArray();
      Assert.True(parts[0].DepthThrough);
      Assert.False(parts[1].DepthThrough);
      Assert.Equal(2.0, parts[1].Depth.Value.Value, 9);
      Assert.False(parts[2].DepthThrough);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Line == parts[2].Line && d.Message.Contains("missing: depth"));
    }

    // ---- Select -------------------------------------------------------------------------

    [Fact]
    public void ASelectPartNeedsOnlyItsLayer()
    {
      var doc = Parse("- LayerName: Shop::Plate\n- actions: select\n");
      Assert.Empty(doc.Diagnostics);
      var part = Assert.Single(doc.AllParts);
      Assert.Equal(PartGenerator.Select, part.Generator);
      Assert.Equal(PartAction.Select, part.PrimaryAction);
      Assert.Equal(PartRole.Keep, part.Role);
      Assert.False(part.HasError);
    }

    [Fact]
    public void ASelectPartWithoutALayerIsAnError()
    {
      var doc = Parse("- actions: select\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("select needs a LayerName"));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Theory]
    [InlineData("select, subtract", "'select, subtract' is planned")]
    [InlineData("select, union", "'select, union' is planned")]
    public void SelectWithARoleIsPlanned(string actions, string message)
    {
      var doc = Parse("- LayerName: Plate\n- actions: " + actions + "\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains(message));
    }

    [Theory]
    [InlineData("BooleanUnion")]
    [InlineData("Loft")]
    public void SelectOutsideBooleanElementsIsPlanned(string type)
    {
      var doc = ListParser.Parse("# Element: " + type + "\n\n## Part\n\n- LayerName: Plate\n- actions: select\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("Boolean and BooleanDifference elements only"));
    }

    [Fact]
    public void DimensionsOnASelectPartGiveOneInfoLine()
    {
      var doc = Parse("- LayerName: Plate\n- shape: square\n- width: 2\n- depth: 1\n- z-position: 3\n- actions: select\n");
      var info = Assert.Single(doc.Diagnostics);
      Assert.Equal(DiagnosticLevel.Info, info.Level);
      Assert.Contains("select reads existing solids", info.Message);
      Assert.False(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void ASelectPartIsWrittenAsItsLayerAndActionOnly()
    {
      var written = Normalize(ListWriter.Write(Parse("- LayerName: Plate\n- LayerColor: Red\n- width: 2\n- actions: select\n")));
      Assert.Contains("## Part\n\n- LayerName: Plate\n- actions: select\n", written);
      Assert.DoesNotContain("orientation", written);
      Assert.DoesNotContain("width", written);
    }

    // ---- The case -----------------------------------------------------------------------

    [Fact]
    public void TheGlobalPropertiesFixtureParsesAsTheCaseDescribes()
    {
      var doc = ListParser.Parse(File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "global-properties.md")));

      var info = Assert.Single(doc.Diagnostics);
      Assert.Equal(DiagnosticLevel.Info, info.Level);
      Assert.Contains("'Pin Connections'", info.Message);

      // First element: the board is selected and twelve cutters go into it.
      var first = doc.Elements[0].Parts;
      Assert.Equal(13, first.Count);
      Assert.Equal(PartGenerator.Select, first[0].Generator);
      Assert.Equal("Sample Module - 5V-12V 2A::Circuit Board::board", first[0].LayerName);
      Assert.All(first.Skip(1), p => Assert.Equal(PartRole.Subtract, p.Role));

      var topPad = first[1];
      Assert.Equal(PartShape.Square, topPad.Shape);
      Assert.Equal(0.1, topPad.Height.Value.Value, 9);
      Assert.Equal(-0.004, topPad.Depth.Value.Value, 9);
      Assert.Equal(0.05, topPad.Offset.Value, 9);
      Assert.Equal(-0.8, topPad.PositionA.Value, 9);
      Assert.Equal(-0.05, first[5].Offset.Value, 9);
      Assert.All(first.Skip(9), p => Assert.True(p.DepthThrough));

      // Second element: the same pads as solids on their own layer, the holes cut through them.
      var second = doc.Elements[1].Parts;
      Assert.Equal(12, second.Count);
      Assert.All(second, p => Assert.Equal("Sample Module - 5V-12V 2A::Circuit Board::circuit_connections", p.LayerName));
      Assert.All(second.Take(8), p => Assert.Equal(PartRole.Keep, p.Role));
      Assert.All(second.Skip(8), p => Assert.Equal(PartRole.Subtract, p.Role));
      Assert.Equal(new[] { 0.05, 0.05, 0.05, 0.05, -0.05, -0.05, -0.05, -0.05 }, second.Take(8).Select(p => p.Offset.Value).ToArray());
      Assert.Equal(new[] { -0.004, -0.004, -0.004, -0.004, 0.004, 0.004, 0.004, 0.004 }, second.Take(8).Select(p => p.Depth.Value.Value).ToArray());
      Assert.Equal("Pin Top Connection [group:pin-top-connections][3]", second[3].DisplayName);
      Assert.All(second.Skip(8), p => Assert.True(p.DepthThrough));
    }
  }
}
