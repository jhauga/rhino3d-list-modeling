using System.Linq;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>Pipe parts, radius stations, action sequences and the depth aliases.</summary>
  [Trait("suite", "unit")]
  public class PipeAndActionTests
  {
    static ListDocument Parse(string body) => ListParser.Parse("# Element: Boolean\n\n## Part\n\n" + body);

    static Part SinglePart(string body) => Assert.Single(Parse(body).AllParts);

    static string Normalize(string text) => text.Replace("\r\n", "\n");

    // ---- Pipes ----------------------------------------------------------------------

    [Fact]
    public void APipeNeedsOnlyACurveAndARadius()
    {
      var doc = Parse("- curve: Rail\n- radius: .125\n- actions: pipe\n");
      Assert.False(doc.HasErrors);

      var part = Assert.Single(doc.AllParts);
      Assert.Equal(PartGenerator.Pipe, part.Generator);
      Assert.Equal("Rail", part.CurveName);
      var station = Assert.Single(part.RadiusStations);
      Assert.Null(station.Parameter);
      Assert.Equal(0.125, station.Radius.Value, 9);
    }

    [Theory]
    [InlineData("curve")]
    [InlineData("rail")]
    public void TheCurveNameKeepsItsSpacesAndCase(string key)
      => Assert.Equal("Curve 1", SinglePart("- " + key + ": Curve 1\n- radius: .1\n- actions: pipe\n").CurveName);

    [Fact]
    public void NestedRadiusLinesAreStationsAlongTheCurve()
    {
      var part = SinglePart("- curve: Rail\n- radius:\n  - 0: .125\n  - 2: .06\n  - 4: .1\n- actions: pipe\n");
      Assert.Equal(new double?[] { 0, 2, 4 }, part.RadiusStations.Select(s => s.Parameter));
      Assert.Equal(new[] { 0.125, 0.06, 0.1 }, part.RadiusStations.Select(s => s.Radius.Value));
      Assert.All(part.RadiusStations, s => Assert.False(s.IsPercent));
      Assert.False(part.HasError);
    }

    [Fact]
    public void StationsMayBePercentagesOfTheCurveLength()
    {
      var part = SinglePart("- curve: Rail\n- radius:\n  - 0%: .1\n  - 50%: .2\n  - 100%: .1\n- actions: pipe\n");
      Assert.Equal(new double?[] { 0, 50, 100 }, part.RadiusStations.Select(s => s.Parameter));
      Assert.All(part.RadiusStations, s => Assert.True(s.IsPercent));
    }

    [Theory]
    [InlineData("- radius: .1\n- actions: pipe\n", "curve")]
    [InlineData("- curve: Rail\n- actions: pipe\n", "radius")]
    [InlineData("- actions: pipe\n", "curve, radius")]
    public void APipeWithoutItsCurveOrRadiusIsAnError(string body, string missing)
    {
      var doc = Parse(body);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("is missing: " + missing + "."));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void APipeDoesNotNeedHeightWidthOrDepth()
      => Assert.DoesNotContain(Parse("- curve: Rail\n- radius: .1\n- actions: pipe\n").Diagnostics,
           d => d.Message.Contains("height") || d.Message.Contains("depth"));

    [Theory]
    [InlineData("  - half: .1\n", "Cannot read radius station")]
    [InlineData("  - 150%: .1\n", "between 0% and 100%")]
    [InlineData("  - 2:\n", "has no radius")]
    [InlineData("  - 2: wide\n", "Cannot read radius")]
    public void AnUnreadableStationIsAnError(string station, string message)
    {
      var doc = Parse("- curve: Rail\n- radius:\n  - 0: .1\n" + station + "- actions: pipe\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains(message));
      Assert.True(Assert.Single(doc.AllParts).HasError);
    }

    [Fact]
    public void AZeroPipeRadiusIsAnError()
    {
      var doc = Parse("- curve: Rail\n- radius: 0\n- actions: pipe\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Error && d.Message.Contains("zero or negative"));
    }

    [Fact]
    public void StationsOnAPartThatIsNotAPipeWarnAndTheFirstRadiusIsUsed()
    {
      var doc = Parse("- radius:\n  - 0: .5\n  - 1: .7\n- depth: 1\n");
      Assert.False(doc.HasErrors);
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("only apply to pipes"));

      var part = Assert.Single(doc.AllParts);
      Assert.Equal(PartShape.Circle, part.Shape);
      Assert.Equal(0.5, part.Width.Value.Value, 9);
    }

    [Fact]
    public void ACurveNameOnAPartThatIsNotAPipeIsNoted()
    {
      var doc = Parse("- height: 1\n- width: 1\n- depth: 1\n- curve: Rail\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Info && d.Message.Contains("only read by pipe parts"));
      Assert.False(doc.HasErrors);
    }

    // ---- Action sequences -----------------------------------------------------------

    const string Everything = "- curve: Rail\n- radius: .1\n- depth: 1\n- actions: ";

    [Theory]
    [InlineData("extrude", PartGenerator.Extrude, PartRole.Keep, PartAction.Extrude)]
    [InlineData("subtract", PartGenerator.Extrude, PartRole.Subtract, PartAction.Subtract)]
    [InlineData("extrude, subtract", PartGenerator.Extrude, PartRole.Subtract, PartAction.Subtract)]
    [InlineData("union", PartGenerator.Extrude, PartRole.Union, PartAction.Union)]
    [InlineData("curve", PartGenerator.Curve, PartRole.Keep, PartAction.Curve)]
    [InlineData("pipe", PartGenerator.Pipe, PartRole.Keep, PartAction.Pipe)]
    [InlineData("tube", PartGenerator.Pipe, PartRole.Keep, PartAction.Pipe)]
    [InlineData("pipe, subtract", PartGenerator.Pipe, PartRole.Subtract, PartAction.Pipe)]
    [InlineData("pipe, union", PartGenerator.Pipe, PartRole.Union, PartAction.Pipe)]
    [InlineData("[pipe, cut]", PartGenerator.Pipe, PartRole.Subtract, PartAction.Pipe)]
    public void ActionsAreAWayToBuildFollowedByARole(string text, PartGenerator generator, PartRole role, PartAction primary)
    {
      var doc = Parse(Everything + text + "\n");
      Assert.DoesNotContain(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning);

      var part = Assert.Single(doc.AllParts);
      Assert.Equal(generator, part.Generator);
      Assert.Equal(role, part.Role);
      Assert.Equal(primary, part.PrimaryAction);
    }

    [Fact]
    public void ACurveCannotCutAndIsKept()
    {
      var doc = Parse("- height: 1\n- width: 1\n- actions: curve, subtract\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("A curve cannot subtract"));
      Assert.Equal(PartRole.Keep, Assert.Single(doc.AllParts).Role);
      Assert.False(doc.HasErrors);
    }

    [Fact]
    public void TwoWaysToBuildWarnAndTheFirstWins()
    {
      var doc = Parse(Everything + "pipe, extrude\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("more than one way"));
      Assert.Equal(PartGenerator.Pipe, Assert.Single(doc.AllParts).Generator);
    }

    [Fact]
    public void TwoRolesWarnAndTheFirstWins()
    {
      var doc = Parse(Everything + "subtract, union\n");
      Assert.Contains(doc.Diagnostics, d => d.Level == DiagnosticLevel.Warning && d.Message.Contains("more than one role"));
      Assert.Equal(PartRole.Subtract, Assert.Single(doc.AllParts).Role);
    }

    [Theory]
    [InlineData("depth")]
    [InlineData("distance")]
    [InlineData("length")]
    [InlineData("thickness")]
    public void DistanceAndLengthAreTheDepth(string key)
    {
      var doc = Parse("- height: 1\n- width: 1\n- " + key + ": 4\n");
      Assert.False(doc.HasErrors);
      Assert.Equal(4.0, Assert.Single(doc.AllParts).Depth.Value.Value, 9);
    }

    // ---- Writing --------------------------------------------------------------------

    [Fact]
    public void TheWriterWritesAPipeAsItsCurveAndRadius()
    {
      var written = Normalize(ListWriter.Write(Parse(
        "- LayerName: \"Pipe\", \"simple\"\n- orientation: top\n- curve: Curve 1\n- radius: .125\n- actions: pipe\n")));

      Assert.Contains("- curve: Curve 1\n", written);
      Assert.Contains("- radius: 0.125\n", written);
      Assert.Contains("- actions: pipe\n", written);
      Assert.DoesNotContain("position", written);
      Assert.DoesNotContain("shape", written);
    }

    [Fact]
    public void TheWriterWritesStationsAsANestedBlock()
    {
      var written = Normalize(ListWriter.Write(Parse(
        "- curve: Rail\n- radius:\n  - 0: .125\n  - 2: .06\n  - 50%: .1\n- actions: pipe\n")));
      Assert.Contains("- radius:\n  - 0: 0.125\n  - 2: 0.06\n  - 50%: 0.1\n", written);
    }

    [Theory]
    [InlineData("extrude", "extrude")]
    [InlineData("extrude, subtract", "subtract")]
    [InlineData("subtract", "subtract")]
    [InlineData("[extrude, union]", "union")]
    [InlineData("pipe, subtract", "pipe, subtract")]
    [InlineData("curve, subtract", "curve")]
    [InlineData("pipe, extrude", "pipe")]
    public void TheWriterWritesTheShortestEquivalentActions(string text, string expected)
      => Assert.Contains("- actions: " + expected + "\n", Normalize(ListWriter.Write(Parse(Everything + text + "\n"))));

    [Fact]
    public void APipeListRoundTrips()
    {
      var source =
        "# Element: Boolean\n\n" +
        "## Rail\n\n- LayerName: Pipe\n  - simple\n- shape: circle\n- radius: 1\n- position: 3, 0\n- actions: curve\n\n" +
        "## Tube\n\n- LayerName: \"Pipe\", \"simple\"\n- curve: Rail\n- radius:\n  - 0: .125\n  - 2: .06\n- actions: pipe, subtract\n";

      var once = ListWriter.Write(ListParser.Parse(source));
      Assert.Equal(once, ListWriter.Write(ListParser.Parse(once)));

      var parts = ListParser.Parse(once).AllParts.ToList();
      Assert.Equal("Pipe::simple", parts[0].LayerName);
      Assert.Equal(PartGenerator.Curve, parts[0].Generator);
      Assert.Equal(PartGenerator.Pipe, parts[1].Generator);
      Assert.Equal(PartRole.Subtract, parts[1].Role);
      Assert.Equal(2, parts[1].RadiusStations.Count);
    }
  }
}
