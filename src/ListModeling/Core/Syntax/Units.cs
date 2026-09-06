using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// Parses "&lt;number&gt;[unit]" values such as ".84in", "10 mm" or "2". A missing unit
  /// resolves to LengthUnit.Model, which the runner maps to the document's model units.
  /// </summary>
  public static class Units
  {
    static readonly Regex MeasurePattern = new Regex(
      @"^\s*(?<num>[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)\s*(?<unit>[A-Za-z""']*)\s*$",
      RegexOptions.Compiled);

    public static bool TryParse(string text, out Measure measure, out string unknownUnit)
    {
      measure = default;
      unknownUnit = null;
      if (string.IsNullOrWhiteSpace(text))
        return false;

      var match = MeasurePattern.Match(text);
      if (!match.Success)
        return false;

      if (!double.TryParse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        return false;

      var unitText = match.Groups["unit"].Value;
      if (!TryParseUnit(unitText, out var unit))
      {
        unknownUnit = unitText;
        unit = LengthUnit.Model;
      }

      measure = new Measure(value, unit);
      return true;
    }

    public static bool TryParseUnit(string text, out LengthUnit unit)
    {
      unit = LengthUnit.Model;
      if (string.IsNullOrWhiteSpace(text))
        return true;

      switch (text.Trim().ToLowerInvariant())
      {
        case "in":
        case "inch":
        case "inches":
        case "\"":
          unit = LengthUnit.Inches;
          return true;
        case "mm":
        case "millimeter":
        case "millimeters":
        case "millimetre":
        case "millimetres":
          unit = LengthUnit.Millimeters;
          return true;
        case "cm":
        case "centimeter":
        case "centimeters":
        case "centimetre":
        case "centimetres":
          unit = LengthUnit.Centimeters;
          return true;
        case "m":
        case "meter":
        case "meters":
        case "metre":
        case "metres":
          unit = LengthUnit.Meters;
          return true;
        case "ft":
        case "feet":
        case "foot":
        case "'":
          unit = LengthUnit.Feet;
          return true;
        default:
          return false;
      }
    }

    /// <summary>The short suffix written back out by the list writer.</summary>
    public static string Suffix(LengthUnit unit)
    {
      switch (unit)
      {
        case LengthUnit.Inches: return "in";
        case LengthUnit.Millimeters: return "mm";
        case LengthUnit.Centimeters: return "cm";
        case LengthUnit.Meters: return "m";
        case LengthUnit.Feet: return "ft";
        default: return string.Empty;
      }
    }

    /// <summary>Length of one unit in meters; Model has no fixed size and returns NaN.</summary>
    public static double MetersPerUnit(LengthUnit unit)
    {
      switch (unit)
      {
        case LengthUnit.Inches: return 0.0254;
        case LengthUnit.Millimeters: return 0.001;
        case LengthUnit.Centimeters: return 0.01;
        case LengthUnit.Meters: return 1.0;
        case LengthUnit.Feet: return 0.3048;
        default: return double.NaN;
      }
    }
  }
}
