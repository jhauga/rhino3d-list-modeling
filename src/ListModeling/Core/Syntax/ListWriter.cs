using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// Writes a ListDocument back out in the same Markdown form the parser reads, so a
  /// list generated from a selection or built step by step can be edited and rerun.
  /// Properties use "- " bullets; nested lines are indented two spaces per level.
  /// </summary>
  public static class ListWriter
  {
    public static string Write(ListDocument doc)
    {
      var sb = new StringBuilder();
      foreach (var element in doc.Elements)
      {
        if (sb.Length > 0)
          sb.AppendLine();
        sb.Append("# ").Append(element.Kind).Append(": ").AppendLine(element.Type);

        string lastGroupParent = null;
        foreach (var part in element.Parts)
        {
          sb.AppendLine();
          if (part.Group != null)
          {
            if (lastGroupParent != part.Name)
            {
              sb.Append("## ").AppendLine(part.Name);
              sb.AppendLine();
              lastGroupParent = part.Name;
            }
            sb.Append("### group:").AppendLine(part.Group);
          }
          else
          {
            sb.Append("## ").AppendLine(part.Name);
            lastGroupParent = null;
          }
          sb.AppendLine();
          WriteProperties(sb, part);
        }
      }
      return sb.ToString();
    }

    static void WriteProperties(StringBuilder sb, Part part)
    {
      if (!string.IsNullOrEmpty(part.LayerName))
        Line(sb, 0, "LayerName: " + LayerText(part.LayerName));
      if (!string.IsNullOrEmpty(part.LayerColor))
        Line(sb, 0, "LayerColor: " + part.LayerColor);
      Line(sb, 0, "orientation: " + part.Orientation.ToString().ToLowerInvariant());

      if (part.Generator == PartGenerator.Pipe)
      {
        // A pipe is its curve and its radius; shape, dimensions and position do not apply.
        if (!string.IsNullOrEmpty(part.CurveName))
          Line(sb, 0, "curve: " + part.CurveName);
        WriteStations(sb, part.RadiusStations);
      }
      else
      {
        if (part.Shape != PartShape.Rectangle)
          Line(sb, 0, "shape: " + ShapeText(part));
        bool radial = part.Shape == PartShape.Circle || part.Shape == PartShape.Polygon;
        if (radial && part.Width.HasValue)
        {
          // Circles and polygons have one radius; the parser mirrors it into width and height.
          Line(sb, 0, "radius: " + part.Width.Value);
        }
        else
        {
          if (part.Height.HasValue)
            Line(sb, 0, "height: " + part.Height.Value);
          if (part.Width.HasValue)
            Line(sb, 0, "width:  " + part.Width.Value);
        }
        if (part.Depth.HasValue)
          Line(sb, 0, "depth:  " + part.Depth.Value);
        Line(sb, 0, "position: " + part.PositionA + ", " + part.PositionB);
        if (part.Offset.Value != 0)
          Line(sb, 0, "offset: " + part.Offset);
        if (!string.IsNullOrEmpty(part.CurveName))
          Line(sb, 0, "curve: " + part.CurveName);
      }

      if (part.Fillet != null && part.Fillet.Selections.Count > 0)
        WriteFillet(sb, part.Fillet);

      Line(sb, 0, "actions: " + ActionsText(part));
      foreach (var extra in part.Extra)
        Line(sb, 0, extra.Key + ": " + extra.Value);
    }

    static void Line(StringBuilder sb, int level, string text)
      => sb.Append(' ', level * 2).Append("- ").AppendLine(text);

    /// <summary>A nested layer path is written as quoted names, the form most lists use.</summary>
    static string LayerText(string layerName)
    {
      if (!layerName.Contains("::"))
        return layerName;
      var segments = layerName.Split(new[] { "::" }, System.StringSplitOptions.RemoveEmptyEntries);
      return string.Join(", ", segments.Select(s => "\"" + s.Trim() + "\""));
    }

    static void WriteStations(StringBuilder sb, List<RadiusStation> stations)
    {
      if (stations.Count == 0)
        return;
      if (stations.Count == 1 && !stations[0].Parameter.HasValue)
      {
        Line(sb, 0, "radius: " + stations[0].Radius);
        return;
      }

      Line(sb, 0, "radius:");
      foreach (var station in stations)
      {
        var parameter = (station.Parameter ?? 0).ToString("0.####", CultureInfo.InvariantCulture) + (station.IsPercent ? "%" : "");
        Line(sb, 1, parameter + ": " + station.Radius);
      }
    }

    static void WriteFillet(StringBuilder sb, FilletSpec fillet)
    {
      Line(sb, 0, "filletEdge:");
      FilletAxis? group = null;
      foreach (var selection in fillet.Selections)
      {
        int level = 1;
        if (selection.Axis == FilletAxis.Extrusion)
        {
          group = null;
        }
        else
        {
          // Consecutive selections on one world axis share a single face group.
          if (group != selection.Axis)
          {
            Line(sb, 1, "select: " + AxisWord(selection.Axis));
            group = selection.Axis;
          }
          level = 2;
        }
        Line(sb, level, "select: " + string.Join(", ", selection.Loops));
        Line(sb, level, "radius: " + string.Join(", ", selection.Radii.Select(RadiusEntryText)));
      }
    }

    static string RadiusEntryText(List<Measure> entry)
      => entry.Count == 1 ? entry[0].ToString() : "[" + string.Join(", ", entry) + "]";

    static string AxisWord(FilletAxis axis)
    {
      switch (axis)
      {
        case FilletAxis.X: return "right";
        case FilletAxis.Y: return "front";
        default: return "top";
      }
    }

    /// <summary>The shortest form that reads back the same: "subtract" rather than "extrude, subtract".</summary>
    static string ActionsText(Part part)
    {
      string role = part.Role == PartRole.Subtract ? "subtract" : part.Role == PartRole.Union ? "union" : null;
      switch (part.Generator)
      {
        case PartGenerator.Curve:
          return "curve";
        case PartGenerator.Pipe:
          return role == null ? "pipe" : "pipe, " + role;
        default:
          return role ?? "extrude";
      }
    }

    static string ShapeText(Part part)
    {
      switch (part.Shape)
      {
        case PartShape.Circle: return "circle";
        case PartShape.Ellipse: return "ellipse";
        case PartShape.Polygon: return "polygon:" + part.PolygonSides;
        default: return "rectangle";
      }
    }
  }
}
