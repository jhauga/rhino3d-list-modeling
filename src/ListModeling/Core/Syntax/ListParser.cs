using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace ListModeling.Core.Syntax
{
  /// <summary>
  /// Reads the Markdown list format into a ListDocument.
  ///
  /// Structure:
  ///   # Element: Type          starts an element (Type is Boolean, BooleanDifference, ...)
  ///   ## Name                  starts a part
  ///   ### group:id             starts a grouped part nested under the current part
  ///   * key: value             a property of the current part ("-" and "+" bullets also work)
  ///
  /// Lines that start with "()->", "()=>" or "&lt;!--" are comments. Code fence lines are
  /// ignored so a list pasted inside a fenced block still parses. Backticks around
  /// header text are stripped.
  /// </summary>
  public static class ListParser
  {
    static readonly Regex HeaderPattern = new Regex(@"^\s{0,3}(?<hashes>#{1,6})\s+(?<text>.+?)\s*#*\s*$", RegexOptions.Compiled);
    static readonly Regex PropertyPattern = new Regex(@"^\s*[-*+]\s+(?<key>[A-Za-z_][A-Za-z0-9_ \-]*?)\s*:\s*(?<value>.*)$", RegexOptions.Compiled);
    static readonly Regex TrailingAnnotation = new Regex(@"\s*\([^()]*\)\s*->.*$", RegexOptions.Compiled);
    static readonly Regex HtmlComment = new Regex(@"<!--.*?-->", RegexOptions.Compiled);

    public static ListDocument Parse(string text)
    {
      var doc = new ListDocument();
      if (text == null)
        return doc;

      Element element = null;
      Part parentPart = null;   // the current "##" part
      Part current = null;      // where property lines go

      var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
      for (int i = 0; i < lines.Length; i++)
      {
        int lineNumber = i + 1;
        var raw = lines[i];
        var line = HtmlComment.Replace(raw, string.Empty);
        var trimmed = line.Trim();

        if (trimmed.Length == 0 || IsComment(trimmed))
          continue;

        var header = HeaderPattern.Match(line);
        if (header.Success)
        {
          int level = header.Groups["hashes"].Value.Length;
          var headerText = CleanHeaderText(header.Groups["text"].Value);

          if (level == 1)
          {
            element = ParseElementHeader(headerText, lineNumber);
            doc.Elements.Add(element);
            parentPart = null;
            current = null;
            continue;
          }

          if (element == null)
          {
            element = new Element("Element", "Boolean", lineNumber);
            doc.Elements.Add(element);
            doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, lineNumber,
              "No '# Element:' header before the first part; assuming '# Element: Boolean'."));
          }

          if (level == 2)
          {
            parentPart = new Part(headerText, lineNumber);
            element.Parts.Add(parentPart);
            current = parentPart;
            continue;
          }

          // Level 3 and deeper: a grouped part under the current "##" part, which
          // becomes a container for it.
          if (parentPart != null)
            parentPart.IsContainer = true;
          var groupPart = new Part(parentPart != null ? parentPart.Name : headerText, lineNumber);
          var groupId = ParseGroupId(headerText);
          if (groupId != null)
            groupPart.Group = groupId;
          else
            groupPart.Name = headerText;
          if (parentPart != null)
            InheritDefaults(groupPart, parentPart);
          element.Parts.Add(groupPart);
          current = groupPart;
          continue;
        }

        var property = PropertyPattern.Match(line);
        if (property.Success)
        {
          if (current == null)
          {
            doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, lineNumber,
              "Property before any '## Part' header is ignored."));
            continue;
          }
          ApplyProperty(current, property.Groups["key"].Value, CleanValue(property.Groups["value"].Value), lineNumber, doc.Diagnostics);
          continue;
        }

        // Anything else (prose, stray text) is ignored but noted once per line.
        doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, lineNumber, "Ignored: " + Truncate(trimmed, 60)));
      }

      RemoveContainerParts(doc);
      Validate(doc);
      return doc;
    }

    static bool IsComment(string trimmed)
      => trimmed.StartsWith("()->", StringComparison.Ordinal)
      || trimmed.StartsWith("()=>", StringComparison.Ordinal)
      || trimmed.StartsWith("<!--", StringComparison.Ordinal)
      || trimmed.StartsWith("```", StringComparison.Ordinal)
      || trimmed.StartsWith("~~~", StringComparison.Ordinal);

    static string CleanHeaderText(string text)
    {
      var cleaned = TrailingAnnotation.Replace(text, string.Empty).Trim();
      cleaned = cleaned.Trim('`').Trim();
      return cleaned;
    }

    static string CleanValue(string value)
    {
      var cleaned = TrailingAnnotation.Replace(value ?? string.Empty, string.Empty).Trim();
      cleaned = cleaned.Trim('`').Trim();
      if (cleaned.Equals("null", StringComparison.OrdinalIgnoreCase) ||
          cleaned.Equals("none", StringComparison.OrdinalIgnoreCase))
        return string.Empty;
      return cleaned;
    }

    static Element ParseElementHeader(string text, int line)
    {
      // "Element: BooleanDifference", "Part: curve", or just "BooleanDifference".
      var colon = text.IndexOf(':');
      if (colon > 0)
      {
        var kind = text.Substring(0, colon).Trim();
        var type = text.Substring(colon + 1).Trim();
        if (kind.Equals("Element", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("Part", StringComparison.OrdinalIgnoreCase))
          return new Element(kind, type, line);
      }
      return new Element("Element", text.Trim(), line);
    }

    static string ParseGroupId(string headerText)
    {
      var match = Regex.Match(headerText, @"^\s*group\s*:\s*(?<id>.+)$", RegexOptions.IgnoreCase);
      return match.Success ? match.Groups["id"].Value.Trim() : null;
    }

    /// <summary>A grouped part starts from the values written directly under its "##" parent.</summary>
    static void InheritDefaults(Part child, Part parent)
    {
      if (!parent.HasProperties)
        return;
      child.LayerName = parent.LayerName;
      child.LayerColor = parent.LayerColor;
      child.Orientation = parent.Orientation;
      child.Shape = parent.Shape;
      child.ShapeSet = parent.ShapeSet;
      child.PolygonSides = parent.PolygonSides;
      child.Height = parent.Height;
      child.Width = parent.Width;
      child.Depth = parent.Depth;
      child.PositionA = parent.PositionA;
      child.PositionB = parent.PositionB;
      child.Offset = parent.Offset;
      child.Actions.AddRange(parent.Actions);
    }

    static void ApplyProperty(Part part, string rawKey, string value, int line, List<Diagnostic> diagnostics)
    {
      part.HasProperties = true;
      var key = rawKey.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

      switch (key)
      {
        case "layername":
        case "layer":
          part.LayerName = value.Length == 0 ? null : value;
          return;

        case "layercolor":
        case "color":
        case "colour":
          part.LayerColor = value.Length == 0 ? null : value;
          return;

        case "orientation":
          if (TryParseOrientation(value, out var orientation))
            part.Orientation = orientation;
          else
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
              $"Unknown orientation '{value}'; using top."));
          return;

        case "shape":
          ApplyShape(part, value, line, diagnostics);
          part.ShapeSet = true;
          return;

        case "sides":
          if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sides) && sides >= 3)
          {
            part.Shape = PartShape.Polygon;
            part.ShapeSet = true;
            part.PolygonSides = sides;
          }
          else
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, $"Invalid polygon side count '{value}'."));
          return;

        case "radius":
        case "r":
          // One value for both plane axes; implies a circle unless a shape was named.
          var radius = ParseMeasure(value, "radius", line, diagnostics);
          if (radius.HasValue)
          {
            part.Width = radius;
            part.Height = radius;
            if (!part.ShapeSet)
              part.Shape = PartShape.Circle;
          }
          return;

        case "height":
          part.Height = ParseMeasure(value, "height", line, diagnostics);
          return;
        case "width":
          part.Width = ParseMeasure(value, "width", line, diagnostics);
          return;
        case "depth":
        case "thickness":
          part.Depth = ParseMeasure(value, "depth", line, diagnostics);
          return;
        case "offset":
        case "elevation":
          var offset = ParseMeasure(value, "offset", line, diagnostics);
          if (offset.HasValue)
            part.Offset = offset.Value;
          return;

        case "position":
        case "pos":
        case "center":
        case "centre":
          ApplyPosition(part, value, line, diagnostics);
          return;

        case "actions":
        case "action":
          ApplyActions(part, value, line, diagnostics);
          return;

        default:
          part.Extra[rawKey.Trim()] = value;
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, line, $"Unknown property '{rawKey.Trim()}' kept but not used."));
          return;
      }
    }

    static void ApplyShape(Part part, string value, int line, List<Diagnostic> diagnostics)
    {
      var text = value.Trim().ToLowerInvariant();
      var polygon = Regex.Match(text, @"^polygon\s*[:(]?\s*(\d+)\s*\)?$");
      if (polygon.Success)
      {
        part.Shape = PartShape.Polygon;
        part.PolygonSides = Math.Max(3, int.Parse(polygon.Groups[1].Value, CultureInfo.InvariantCulture));
        return;
      }

      switch (text)
      {
        case "rectangle":
        case "rect":
        case "square":
        case "box":
          part.Shape = PartShape.Rectangle;
          return;
        case "circle":
        case "round":
        case "cylinder":
          part.Shape = PartShape.Circle;
          return;
        case "ellipse":
        case "oval":
          part.Shape = PartShape.Ellipse;
          return;
        case "triangle":
          part.Shape = PartShape.Polygon; part.PolygonSides = 3; return;
        case "pentagon":
          part.Shape = PartShape.Polygon; part.PolygonSides = 5; return;
        case "hexagon":
        case "polygon":
          part.Shape = PartShape.Polygon; part.PolygonSides = 6; return;
        case "octagon":
          part.Shape = PartShape.Polygon; part.PolygonSides = 8; return;
        default:
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, $"Unknown shape '{value}'; using rectangle."));
          return;
      }
    }

    static Measure? ParseMeasure(string value, string name, int line, List<Diagnostic> diagnostics)
    {
      if (value.Length == 0)
        return null;
      if (!Units.TryParse(value, out var measure, out var unknownUnit))
      {
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, line, $"Cannot read {name} '{value}'."));
        return null;
      }
      if (unknownUnit != null)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, $"Unknown unit '{unknownUnit}' on {name}; using model units."));
      return measure;
    }

    static void ApplyPosition(Part part, string value, int line, List<Diagnostic> diagnostics)
    {
      var parts = value.Trim().Trim('[', ']', '(', ')').Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length < 2)
        parts = value.Trim().Trim('[', ']', '(', ')').Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

      if (parts.Length < 2)
      {
        // Resolvable to a default, so this warns and the part is still built, the same
        // way an unreadable color or orientation does.
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
          $"Position needs two values, got '{value}'; using 0, 0."));
        return;
      }

      var a = ParseMeasure(parts[0].Trim(), "position", line, diagnostics);
      var b = ParseMeasure(parts[1].Trim(), "position", line, diagnostics);
      if (!a.HasValue || !b.HasValue)
        return;

      part.PositionA = a.Value;
      part.PositionB = b.Value;

      if (parts.Length > 2)
      {
        var c = ParseMeasure(parts[2].Trim(), "offset", line, diagnostics);
        if (c.HasValue)
          part.Offset = c.Value;
      }
    }

    static void ApplyActions(Part part, string value, int line, List<Diagnostic> diagnostics)
    {
      part.Actions.Clear();
      var tokens = value.Trim().Trim('[', ']').Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (var token in tokens)
      {
        switch (token.Trim().ToLowerInvariant())
        {
          case "extrude":
          case "solid":
            part.Actions.Add(PartAction.Extrude);
            break;
          case "subtract":
          case "cut":
          case "difference":
            part.Actions.Add(PartAction.Subtract);
            break;
          case "union":
          case "add":
          case "join":
            part.Actions.Add(PartAction.Union);
            break;
          case "curve":
          case "profile":
          case "outline":
            part.Actions.Add(PartAction.Curve);
            break;
          case "surface":
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
              "Action 'surface' is planned; the profile curve is created instead."));
            part.Actions.Add(PartAction.Curve);
            break;
          default:
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, $"Unknown action '{token}' ignored."));
            break;
        }
      }
      if (part.Actions.Count == 0)
        part.Actions.Add(PartAction.Extrude);
    }

    public static bool TryParseOrientation(string value, out Orientation orientation)
    {
      orientation = Orientation.Top;
      switch ((value ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "top": orientation = Orientation.Top; return true;
        case "bottom": orientation = Orientation.Bottom; return true;
        case "front": orientation = Orientation.Front; return true;
        case "back": orientation = Orientation.Back; return true;
        case "right": orientation = Orientation.Right; return true;
        case "left":
        case "side": orientation = Orientation.Left; return true;
        default: return false;
      }
    }

    /// <summary>
    /// A header with deeper headers under it labels those parts rather than describing
    /// one of its own, so it is dropped once its children have inherited what it
    /// declared. Dimensions on a container are defaults for the children, never a
    /// request for an extra solid at the container's own position.
    /// </summary>
    static void RemoveContainerParts(ListDocument doc)
    {
      foreach (var element in doc.Elements)
        element.Parts.RemoveAll(p => p.IsContainer);
    }

    static void Validate(ListDocument doc)
    {
      foreach (var element in doc.Elements)
      {
        if (element.Parts.Count == 0)
          doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, element.Line, $"Element '{element.Type}' has no parts."));

        if (element.Family == ElementFamily.Unknown)
          doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, element.Line,
            $"Unknown element type '{element.Type}'; parts are built as solids."));

        foreach (var part in element.Parts)
        {
          if (part.Actions.Count == 0)
            part.Actions.Add(PartAction.Extrude);

          var missing = new List<string>();
          if (!part.Height.HasValue) missing.Add("height");
          if (!part.Width.HasValue) missing.Add("width");
          // Matches the runner's rule: only a part whose primary action is curve may
          // omit depth. Testing HasAction here instead let "extrude, curve" through
          // validation and the runner then dropped the part with no message.
          if (!part.Depth.HasValue && part.PrimaryAction != PartAction.Curve) missing.Add("depth");
          if (missing.Count > 0)
          {
            part.HasError = true;
            doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
              $"Part '{part.DisplayName}' is missing: {string.Join(", ", missing)}."));
          }

          if (part.Height.HasValue && part.Height.Value.Value <= 0 ||
              part.Width.HasValue && part.Width.Value.Value <= 0 ||
              part.Depth.HasValue && part.Depth.Value.Value <= 0)
          {
            part.HasError = true;
            doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
              $"Part '{part.DisplayName}' has a zero or negative dimension."));
          }
        }
      }
    }

    static string Truncate(string text, int max)
      => text.Length <= max ? text : text.Substring(0, max) + "...";
  }
}
