using System.Linq;
using System.Text;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// Writes a ListDocument back out in the same Markdown form the parser reads, so a
  /// list generated from a selection or built step by step can be edited and rerun.
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
        sb.Append("* LayerName: ").AppendLine(part.LayerName);
      if (!string.IsNullOrEmpty(part.LayerColor))
        sb.Append("* LayerColor: ").AppendLine(part.LayerColor);
      sb.Append("* orientation: ").AppendLine(part.Orientation.ToString().ToLowerInvariant());
      if (part.Shape != PartShape.Rectangle)
        sb.Append("* shape: ").AppendLine(ShapeText(part));
      bool radial = part.Shape == PartShape.Circle || part.Shape == PartShape.Polygon;
      if (radial && part.Width.HasValue)
      {
        // Circles and polygons have one radius; the parser mirrors it into width and height.
        sb.Append("* radius: ").AppendLine(part.Width.Value.ToString());
      }
      else
      {
        if (part.Height.HasValue)
          sb.Append("* height: ").AppendLine(part.Height.Value.ToString());
        if (part.Width.HasValue)
          sb.Append("* width:  ").AppendLine(part.Width.Value.ToString());
      }
      if (part.Depth.HasValue)
        sb.Append("* depth:  ").AppendLine(part.Depth.Value.ToString());
      sb.Append("* position: ").Append(part.PositionA.ToString()).Append(", ").AppendLine(part.PositionB.ToString());
      if (part.Offset.Value != 0)
        sb.Append("* offset: ").AppendLine(part.Offset.ToString());
      sb.Append("* actions: ").AppendLine(string.Join(", ", part.Actions.Select(a => a.ToString().ToLowerInvariant())));
      foreach (var extra in part.Extra)
        sb.Append("* ").Append(extra.Key).Append(": ").AppendLine(extra.Value);
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
