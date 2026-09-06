using System.Drawing;
using ListModeling.Core.Syntax;
using Xunit;

namespace ListModeling.Syntax.Tests
{
  /// <summary>LayerColor values: names, hex triplets and channel lists.</summary>
  public class ColorParserTests
  {
    [Theory]
    [InlineData("Dark Blue", 0, 0, 139)]
    [InlineData("darkblue", 0, 0, 139)]
    [InlineData("dark_blue", 0, 0, 139)]
    [InlineData("dark-blue", 0, 0, 139)]
    [InlineData("  DARKBLUE  ", 0, 0, 139)]
    [InlineData("Brown", 165, 42, 42)]
    public void ParsesKnownColorNames(string text, int r, int g, int b)
    {
      Assert.True(ColorParser.TryParse(text, out var color));
      Assert.Equal((r, g, b), Channels(color));
    }

    static (int R, int G, int B) Channels(Color color) => (color.R, color.G, color.B);

    [Theory]
    [InlineData("#1f2a44", 31, 42, 68)]
    [InlineData("1f2a44", 31, 42, 68)]
    [InlineData("#FA0", 255, 170, 0)]
    [InlineData("#000000", 0, 0, 0)]
    public void ParsesHexTriplets(string text, int r, int g, int b)
    {
      Assert.True(ColorParser.TryParse(text, out var color));
      Assert.Equal((r, g, b), Channels(color));
    }

    [Theory]
    [InlineData("31, 42, 68")]
    [InlineData("31,42,68")]
    [InlineData("31 42 68")]
    [InlineData("rgb(31, 42, 68)")]
    [InlineData("RGB(31,42,68)")]
    public void ParsesChannelLists(string text)
    {
      Assert.True(ColorParser.TryParse(text, out var color));
      Assert.Equal((31, 42, 68), Channels(color));
    }

    [Theory]
    [InlineData("notacolor")]
    [InlineData("300, 0, 0")]
    [InlineData("#12345")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsValuesItCannotRead(string text)
      => Assert.False(ColorParser.TryParse(text, out _));

    [Fact]
    public void ParsedColorsAreFullyOpaque()
    {
      Assert.True(ColorParser.TryParse("#1f2a44", out var color));
      Assert.Equal(255, color.A);
    }

    [Fact]
    public void DisplayStringPrefersTheKnownName()
      => Assert.Equal("Dark Blue", ColorParser.ToDisplayString(Color.FromArgb(0, 0, 139)));

    [Fact]
    public void DisplayStringFallsBackToHex()
      => Assert.Equal("#1f2a45", ColorParser.ToDisplayString(Color.FromArgb(31, 42, 69)));

    [Theory]
    [InlineData(0, 0, 139)]
    [InlineData(165, 42, 42)]
    [InlineData(31, 42, 69)]
    [InlineData(46, 108, 176)]
    public void DisplayStringRoundTripsBackToTheSameChannels(int r, int g, int b)
    {
      var display = ColorParser.ToDisplayString(Color.FromArgb(r, g, b));
      Assert.True(ColorParser.TryParse(display, out var reparsed));
      Assert.Equal((r, g, b), Channels(reparsed));
    }
  }
}
