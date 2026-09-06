using System;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// Turns a LayerColor value into a color. Accepts .NET color names with or without
  /// spaces ("Dark Blue", "darkblue"), hex ("#1f2a44", "#fa0"), and channel lists
  /// ("31, 42, 68" or "rgb(31,42,68)"). Anything else fails, and the caller falls back
  /// to the default layer color.
  /// </summary>
  public static class ColorParser
  {
    static readonly Regex HexPattern = new Regex(@"^#?([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.Compiled);
    static readonly Regex ChannelPattern = new Regex(
      @"^(?:rgb\s*\(\s*)?(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*\)?$",
      RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool TryParse(string text, out Color color)
    {
      color = Color.Empty;
      if (string.IsNullOrWhiteSpace(text))
        return false;

      var value = text.Trim();

      var hex = HexPattern.Match(value);
      if (hex.Success)
      {
        var digits = hex.Groups[1].Value;
        if (digits.Length == 3)
          digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);
        var rgb = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        return true;
      }

      var channels = ChannelPattern.Match(value);
      if (channels.Success)
      {
        int r = int.Parse(channels.Groups[1].Value, CultureInfo.InvariantCulture);
        int g = int.Parse(channels.Groups[2].Value, CultureInfo.InvariantCulture);
        int b = int.Parse(channels.Groups[3].Value, CultureInfo.InvariantCulture);
        if (r > 255 || g > 255 || b > 255)
          return false;
        color = Color.FromArgb(255, r, g, b);
        return true;
      }

      // Named colors: "Dark Blue" and "dark_blue" both mean DarkBlue.
      var name = value.Replace(" ", "").Replace("_", "").Replace("-", "");
      var named = Color.FromName(name);
      if (named.IsKnownColor && !named.IsSystemColor)
      {
        color = named;
        return true;
      }

      return false;
    }

    /// <summary>
    /// A readable form for writing lists: the known color name when the channels match
    /// one exactly, otherwise a hex triplet.
    /// </summary>
    public static string ToDisplayString(Color color)
    {
      foreach (KnownColor known in Enum.GetValues(typeof(KnownColor)))
      {
        var candidate = Color.FromKnownColor(known);
        if (candidate.IsSystemColor)
          continue;
        if (candidate.R == color.R && candidate.G == color.G && candidate.B == color.B)
          return SplitCamelCase(known.ToString());
      }
      return "#" + color.R.ToString("x2") + color.G.ToString("x2") + color.B.ToString("x2");
    }

    static string SplitCamelCase(string name)
      => Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
  }
}
