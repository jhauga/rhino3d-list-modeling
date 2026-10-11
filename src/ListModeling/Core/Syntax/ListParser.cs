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
  ///   - key: value             a property of the current part ("*" and "+" bullets also work)
  ///     - key: value           a nested line, read by the less indented bullet above it
  ///
  /// filletEdge, radius (pipe stations) and LayerName (nested layers) read their nested
  /// lines. Under any other property, nested lines are applied as ordinary properties,
  /// so a list indented unevenly still reads the way it always did.
  ///
  /// Lines that start with "()->", "()=>" or "&lt;!--" are comments, and so are block quotes
  /// ("&gt; note") and thematic breaks ("---"). HTML comments may span several lines. Code
  /// fence lines are ignored so a list pasted inside a fenced block still parses.
  /// Backticks around header text are stripped.
  /// </summary>
  public static class ListParser
  {
    static readonly Regex HeaderPattern = new Regex(@"^\s{0,3}(?<hashes>#{1,6})\s+(?<text>.+?)\s*#*\s*$", RegexOptions.Compiled);
    static readonly Regex BulletPattern = new Regex(@"^(?<indent>[ \t]*)[-*+][ \t]+(?<body>.*)$", RegexOptions.Compiled);
    static readonly Regex KeyValuePattern = new Regex(@"^(?<key>[A-Za-z0-9_.][A-Za-z0-9_ .%\-]*?)\s*:\s*(?<value>.*)$", RegexOptions.Compiled);
    static readonly Regex ThematicBreak = new Regex(@"^ {0,3}([-*_])[ \t]*(?:\1[ \t]*){2,}$", RegexOptions.Compiled);
    static readonly Regex TrailingAnnotation = new Regex(@"\s*\([^()]*\)\s*->.*$", RegexOptions.Compiled);
    static readonly Regex HtmlComment = new Regex(@"<!--.*?-->", RegexOptions.Compiled);
    static readonly Regex QuotedSegment = new Regex("\"([^\"]*)\"", RegexOptions.Compiled);
    static readonly Regex StationParameter = new Regex(@"^\s*(?<num>[-+]?(?:\d+\.?\d*|\.\d+))\s*(?<percent>%)?\s*$", RegexOptions.Compiled);

    /// <summary>One bullet line and the bullets nested under it.</summary>
    sealed class Node
    {
      public Node(int indent, int line, string key, string value)
      {
        Indent = indent;
        Line = line;
        Key = key;
        Value = value;
      }

      public int Indent { get; }
      public int Line { get; }
      /// <summary>The key as written; null for a bullet without "key:".</summary>
      public string Key { get; }
      /// <summary>The cleaned value, or the cleaned bullet text when there is no key.</summary>
      public string Value { get; }
      public List<Node> Children { get; } = new List<Node>();
    }

    public static ListDocument Parse(string text)
    {
      var doc = new ListDocument();
      if (text == null)
        return doc;

      Element element = null;
      Part parentPart = null;   // the current "##" part
      Part current = null;      // where property lines go

      // Top level property lines of the current part wait here until the next line at
      // their level shows that nothing more is nested under them.
      var pending = new List<Node>();
      var open = new Stack<Node>();
      bool inHtmlComment = false;

      void ApplyPending()
      {
        foreach (var node in pending)
          ApplyProperty(current, node, doc.Diagnostics);
        pending.Clear();
        open.Clear();
      }

      var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
      for (int i = 0; i < lines.Length; i++)
      {
        int lineNumber = i + 1;
        var line = lines[i];

        if (inHtmlComment)
        {
          int close = line.IndexOf("-->", StringComparison.Ordinal);
          if (close < 0)
            continue;
          line = line.Substring(close + 3);
          inHtmlComment = false;
        }
        line = HtmlComment.Replace(line, string.Empty);
        int opener = line.IndexOf("<!--", StringComparison.Ordinal);
        if (opener >= 0)
        {
          inHtmlComment = true;
          line = line.Substring(0, opener);
        }

        var trimmed = line.Trim();
        if (trimmed.Length == 0 || IsComment(trimmed) || ThematicBreak.IsMatch(line))
          continue;

        var header = HeaderPattern.Match(line);
        if (header.Success)
        {
          ApplyPending();
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

        var bullet = BulletPattern.Match(line);
        if (bullet.Success)
        {
          var node = ReadNode(IndentWidth(bullet.Groups["indent"].Value), lineNumber, bullet.Groups["body"].Value);

          if (current == null)
          {
            if (IsPropertyKey(node.Key))
              doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, lineNumber,
                "Property before any '## Part' header is ignored."));
            else
              doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, lineNumber, "Ignored: " + Truncate(trimmed, 60)));
            continue;
          }

          while (open.Count > 0 && open.Peek().Indent >= node.Indent)
            open.Pop();

          if (open.Count == 0)
          {
            ApplyPending();
            if (!IsPropertyKey(node.Key))
            {
              doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, lineNumber, "Ignored: " + Truncate(trimmed, 60)));
              continue;
            }
            pending.Add(node);
          }
          else
          {
            open.Peek().Children.Add(node);
          }
          open.Push(node);
          continue;
        }

        // Anything else (prose, stray text) is ignored but noted once per line.
        ApplyPending();
        doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, lineNumber, "Ignored: " + Truncate(trimmed, 60)));
      }
      ApplyPending();

      RemoveContainerParts(doc);
      Validate(doc);
      return doc;
    }

    static bool IsComment(string trimmed)
      => trimmed.StartsWith("()->", StringComparison.Ordinal)
      || trimmed.StartsWith("()=>", StringComparison.Ordinal)
      || trimmed.StartsWith("<!--", StringComparison.Ordinal)
      || trimmed.StartsWith("```", StringComparison.Ordinal)
      || trimmed.StartsWith("~~~", StringComparison.Ordinal)
      || trimmed.StartsWith(">", StringComparison.Ordinal);

    /// <summary>Leading whitespace width, a tab counting as two spaces. Only comparisons matter.</summary>
    static int IndentWidth(string indent)
    {
      int width = 0;
      foreach (var c in indent)
        width += c == '\t' ? 2 : 1;
      return width;
    }

    static Node ReadNode(int indent, int line, string body)
    {
      var pair = KeyValuePattern.Match(body);
      return pair.Success
        ? new Node(indent, line, pair.Groups["key"].Value.Trim(), CleanValue(pair.Groups["value"].Value))
        : new Node(indent, line, null, CleanValue(body));
    }

    /// <summary>Property keys start with a letter or underscore; "0: .1" only makes sense nested.</summary>
    static bool IsPropertyKey(string key)
      => !string.IsNullOrEmpty(key) && (key[0] == '_' || (key[0] >= 'A' && key[0] <= 'Z') || (key[0] >= 'a' && key[0] <= 'z'));

    static string NormalizeKey(string key)
      => (key ?? string.Empty).Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

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
      child.CurveName = parent.CurveName;
      child.RadiusStations.AddRange(parent.RadiusStations);
      child.Fillet = parent.Fillet?.Clone();
      child.Actions.AddRange(parent.Actions);
      // A value the container could not read is a value every child would build wrong.
      child.HasError = parent.HasError;
    }

    // ---- Properties ---------------------------------------------------------------

    static void ApplyProperty(Part part, Node node, List<Diagnostic> diagnostics)
    {
      part.HasProperties = true;
      var key = NormalizeKey(node.Key);
      var value = node.Value;
      int line = node.Line;

      switch (key)
      {
        case "layername":
        case "layer":
          part.LayerName = ReadLayerPath(part, node, diagnostics);
          return;

        case "radius":
        case "r":
          ApplyRadius(part, node, diagnostics);
          return;

        case "filletedge":
        case "filletedges":
        case "fillet":
        case "fillets":
          ApplyFillet(part, node, diagnostics);
          return;

        case "layercolor":
        case "color":
        case "colour":
          part.LayerColor = value.Length == 0 ? null : value;
          break;

        case "orientation":
          if (TryParseOrientation(value, out var orientation))
            part.Orientation = orientation;
          else
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
              $"Unknown orientation '{value}'; using top."));
          break;

        case "shape":
          ApplyShape(part, value, line, diagnostics);
          part.ShapeSet = true;
          break;

        case "sides":
          if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sides) && sides >= 3)
          {
            part.Shape = PartShape.Polygon;
            part.ShapeSet = true;
            part.PolygonSides = sides;
          }
          else
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, $"Invalid polygon side count '{value}'."));
          break;

        case "height":
          part.Height = ParseMeasure(part, value, "height", line, diagnostics);
          break;
        case "width":
          part.Width = ParseMeasure(part, value, "width", line, diagnostics);
          break;
        case "depth":
        case "thickness":
        case "distance":
        case "length":
          part.Depth = ParseMeasure(part, value, "depth", line, diagnostics);
          break;
        case "offset":
        case "elevation":
          var offset = ParseMeasure(part, value, "offset", line, diagnostics);
          if (offset.HasValue)
            part.Offset = offset.Value;
          break;

        case "position":
        case "pos":
        case "center":
        case "centre":
          ApplyPosition(part, value, line, diagnostics);
          break;

        case "actions":
        case "action":
          ApplyActions(part, value, line, diagnostics);
          break;

        case "curve":
        case "rail":
          part.CurveName = value.Length == 0 ? null : value;
          break;

        default:
          // Unknown keys are kept for the writer; nested lines under them are kept too,
          // flattened to "key.child" so they still round trip.
          if (value.Length > 0 || node.Children.Count == 0)
            part.Extra[node.Key.Trim()] = value;
          FlattenExtra(part, node.Key.Trim(), node.Children);
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, line, $"Unknown property '{node.Key.Trim()}' kept but not used."));
          return;
      }

      // A single-value property has nothing to read from nested lines, so they are applied
      // to the part as if they had been written at the top level.
      ApplyChildrenAsProperties(part, node.Children, diagnostics);
    }

    static void ApplyChildrenAsProperties(Part part, IEnumerable<Node> children, List<Diagnostic> diagnostics)
    {
      foreach (var child in children)
      {
        if (IsPropertyKey(child.Key))
          ApplyProperty(part, child, diagnostics);
        else
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, child.Line, "Ignored: " + Truncate(DescribeNode(child), 60)));
      }
    }

    static void FlattenExtra(Part part, string prefix, IEnumerable<Node> children)
    {
      foreach (var child in children)
      {
        var name = prefix + "." + (child.Key ?? child.Value).Trim();
        if (child.Key != null && (child.Value.Length > 0 || child.Children.Count == 0))
          part.Extra[name] = child.Value;
        FlattenExtra(part, name, child.Children);
      }
    }

    static string DescribeNode(Node node)
      => node.Key == null ? node.Value : node.Key + ": " + node.Value;

    // ---- Layer paths ----------------------------------------------------------------

    /// <summary>
    /// Reads "Parent::Child", "\"Parent\", \"Child\"", or a name with the child layer as
    /// a nested bullet, into one "::" path. Nested lines with a key are ordinary properties.
    /// </summary>
    static string ReadLayerPath(Part part, Node node, List<Diagnostic> diagnostics)
    {
      var segments = new List<string>();
      AddLayerSegments(segments, node.Value);

      var level = node;
      for (;;)
      {
        Node next = null;
        foreach (var child in level.Children)
        {
          if (child.Key != null)
          {
            ApplyChildrenAsProperties(part, new[] { child }, diagnostics);
            continue;
          }
          if (next == null)
            next = child;
          else
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, child.Line,
              $"LayerName lists more than one nested layer at the same level; using '{next.Value}', '{child.Value}' is ignored."));
        }
        if (next == null)
          break;
        AddLayerSegments(segments, next.Value);
        level = next;
      }

      return segments.Count == 0 ? null : string.Join("::", segments);
    }

    static void AddLayerSegments(List<string> segments, string text)
    {
      if (string.IsNullOrWhiteSpace(text))
        return;

      // Quotes split segments only when the value is nothing but quoted names and commas,
      // so a plain name that happens to contain a comma stays one layer.
      IEnumerable<string> pieces = new[] { text };
      var quoted = QuotedSegment.Matches(text);
      if (quoted.Count > 0 && Regex.IsMatch(QuotedSegment.Replace(text, string.Empty), @"^[\s,]*$"))
        pieces = quoted.Cast<Match>().Select(m => m.Groups[1].Value);

      foreach (var piece in pieces)
      {
        foreach (var segment in piece.Split(new[] { "::" }, StringSplitOptions.None))
        {
          var name = segment.Trim().Trim('"').Trim();
          if (name.Length > 0)
            segments.Add(name);
        }
      }
    }

    // ---- Radius and pipe stations ---------------------------------------------------

    /// <summary>
    /// "radius: 2" is one radius for the plane axes (and implies a circle when no shape is
    /// named), and is also the constant radius of a pipe. A nested block of
    /// "parameter: radius" lines gives a pipe a radius that changes along its curve.
    /// </summary>
    static void ApplyRadius(Part part, Node node, List<Diagnostic> diagnostics)
    {
      if (node.Value.Length > 0)
      {
        var radius = ParseMeasure(part, node.Value, "radius", node.Line, diagnostics);
        if (radius.HasValue)
        {
          part.Width = radius;
          part.Height = radius;
          if (!part.ShapeSet)
            part.Shape = PartShape.Circle;
          part.RadiusStations.Clear();
          part.RadiusStations.Add(new RadiusStation(null, false, radius.Value));
        }
        // With a value of its own, radius reads nothing nested, the same as any other
        // single-value property.
        ApplyChildrenAsProperties(part, node.Children, diagnostics);
        return;
      }

      if (node.Children.Count == 0)
        return;

      var stations = new List<RadiusStation>();
      foreach (var child in node.Children)
      {
        var match = child.Key == null ? Match.Empty : StationParameter.Match(child.Key);
        if (!match.Success)
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, child.Line,
            $"Cannot read radius station '{DescribeNode(child)}'; write it as 'position: radius'."));
          part.HasError = true;
          continue;
        }

        double parameter = double.Parse(match.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        bool percent = match.Groups["percent"].Success;
        if (percent && (parameter < 0 || parameter > 100))
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, child.Line,
            $"Radius station '{child.Key}' must be between 0% and 100%."));
          part.HasError = true;
          continue;
        }

        if (child.Value.Length == 0)
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, child.Line,
            $"Radius station '{child.Key}' has no radius."));
          part.HasError = true;
          continue;
        }

        var radius = ParseMeasure(part, child.Value, "radius", child.Line, diagnostics);
        if (radius.HasValue)
          stations.Add(new RadiusStation(parameter, percent, radius.Value));
      }

      part.RadiusStations.Clear();
      part.RadiusStations.AddRange(stations);
    }

    // ---- Fillets ----------------------------------------------------------------------

    /// <summary>
    /// Reads a filletEdge block. Each "select" picks loops by index (0 the start, 1 the
    /// end) and the "radius" after it gives one value per loop, or a bracketed list that
    /// goes edge by edge. "select: top", "front" or "right" opens a group that counts loops
    /// along a world axis instead of along the part's depth.
    /// </summary>
    static void ApplyFillet(Part part, Node node, List<Diagnostic> diagnostics)
    {
      if (node.Value.Length > 0)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Line,
          $"filletEdge takes nested 'select' and 'radius' lines; '{node.Value}' is ignored."));

      var spec = new FilletSpec();
      ReadFilletLines(part, node.Children, FilletAxis.Extrusion, spec, diagnostics);

      foreach (var selection in spec.Selections.ToList())
      {
        if (selection.Loops.Count == 0)
        {
          spec.Selections.Remove(selection);
          continue;
        }
        if (selection.Radii.Count == 0)
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, selection.Line,
            "Fillet select has no radius; those edges are not filleted."));
          spec.Selections.Remove(selection);
          continue;
        }
        if (selection.Radii.Any(entry => entry.Any(m => m.Value <= 0)))
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, selection.Line,
            "Fillet radius must be greater than zero; those edges are not filleted."));
          spec.Selections.Remove(selection);
          continue;
        }
        if (selection.Radii.Count > selection.Loops.Count)
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, selection.Line,
            $"Fillet select names {selection.Loops.Count} loop(s) but gives {selection.Radii.Count} radius entries; the extra entries are ignored."));
          selection.Radii.RemoveRange(selection.Loops.Count, selection.Radii.Count - selection.Loops.Count);
        }
      }

      if (spec.Selections.Count == 0)
      {
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Line,
          "filletEdge has no usable 'select' and 'radius' pair; no edges are filleted."));
        part.Fillet = null;
        return;
      }
      part.Fillet = spec;
    }

    static void ReadFilletLines(Part part, List<Node> nodes, FilletAxis axis, FilletSpec spec, List<Diagnostic> diagnostics)
    {
      FilletSelection last = null;
      List<List<Measure>> waitingRadii = null;
      int waitingLine = 0;

      foreach (var node in nodes)
      {
        var key = node.Key == null ? null : NormalizeKey(node.Key);

        if (key == "select" || key == "edges" || key == "edge" || key == "loops")
        {
          if (TryParseFaceAxis(node.Value, out var faceAxis, out var canonical, out var isAlias))
          {
            last = null;
            if (axis != FilletAxis.Extrusion)
            {
              diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Line,
                $"'select: {node.Value}' cannot sit inside another face group; it is ignored."));
              continue;
            }
            if (isAlias)
              diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, node.Line,
                $"'select: {node.Value}' counts loops along the same axis as '{canonical}': 0 is the lowest loop, 1 the highest."));
            if (node.Children.Count == 0)
              diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Line,
                $"'select: {node.Value}' needs nested 'select' and 'radius' lines."));
            ReadFilletLines(part, node.Children, faceAxis, spec, diagnostics);
            continue;
          }

          last = new FilletSelection(axis, node.Line);
          last.Loops.AddRange(ReadLoops(node.Value, node.Line, diagnostics));
          spec.Selections.Add(last);
          if (waitingRadii != null)
          {
            last.Radii.AddRange(waitingRadii);
            waitingRadii = null;
          }
          if (node.Children.Count > 0)
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Children[0].Line,
              "Lines nested under a numbered select are ignored."));
          continue;
        }

        if (key == "radius" || key == "r" || key == "radii")
        {
          var radii = ReadRadiusList(part, node.Value, node.Line, diagnostics);
          if (last != null && last.Radii.Count == 0)
            last.Radii.AddRange(radii);
          else if (last == null && waitingRadii == null)
          {
            waitingRadii = radii;
            waitingLine = node.Line;
          }
          else
            diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Line,
              "This select already has a radius; the second one is ignored."));
          continue;
        }

        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, node.Line,
          $"Unknown filletEdge line '{Truncate(DescribeNode(node), 40)}' ignored."));
      }

      if (waitingRadii != null)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, waitingLine,
          "Fillet radius has no select to go with; it is ignored."));
    }

    static List<int> ReadLoops(string text, int line, List<Diagnostic> diagnostics)
    {
      var loops = new List<int>();
      foreach (var token in StripEnclosing(text).Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
      {
        if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && (index == 0 || index == 1))
        {
          if (!loops.Contains(index))
            loops.Add(index);
        }
        else
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
            $"Fillet select '{token.Trim()}' is not 0 (the start loop) or 1 (the end loop); it is ignored."));
      }
      if (loops.Count == 0)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, "Fillet select names no loop; nothing is filleted for it."));
      return loops;
    }

    /// <summary>".1, .02" is one value per loop; "[.1, .2], .3" fillets the first loop edge by edge.</summary>
    static List<List<Measure>> ReadRadiusList(Part part, string text, int line, List<Diagnostic> diagnostics)
    {
      var result = new List<List<Measure>>();
      foreach (var item in SplitTopLevel(text))
      {
        var entry = new List<Measure>();
        var inner = item.StartsWith("[", StringComparison.Ordinal) && item.EndsWith("]", StringComparison.Ordinal)
          ? SplitTopLevel(item.Substring(1, item.Length - 2))
          : new List<string> { item };
        foreach (var value in inner)
        {
          var measure = ParseMeasure(part, value, "fillet radius", line, diagnostics);
          if (measure.HasValue)
            entry.Add(measure.Value);
        }
        if (entry.Count > 0)
          result.Add(entry);
      }
      return result;
    }

    /// <summary>Splits on commas that are not inside brackets or parentheses.</summary>
    static List<string> SplitTopLevel(string text)
    {
      var pieces = new List<string>();
      int depth = 0, start = 0;
      for (int i = 0; i < text.Length; i++)
      {
        char c = text[i];
        if (c == '[' || c == '(') depth++;
        else if (c == ']' || c == ')') depth = Math.Max(0, depth - 1);
        else if (c == ',' && depth == 0)
        {
          pieces.Add(text.Substring(start, i - start));
          start = i + 1;
        }
      }
      pieces.Add(text.Substring(start));
      return pieces.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    }

    static bool TryParseFaceAxis(string value, out FilletAxis axis, out string canonical, out bool isAlias)
    {
      isAlias = false;
      switch ((value ?? string.Empty).Trim().ToLowerInvariant())
      {
        case "top": axis = FilletAxis.Z; canonical = "top"; return true;
        case "z": axis = FilletAxis.Z; canonical = "top"; return true;
        case "bottom": axis = FilletAxis.Z; canonical = "top"; isAlias = true; return true;
        case "front": axis = FilletAxis.Y; canonical = "front"; return true;
        case "y": axis = FilletAxis.Y; canonical = "front"; return true;
        case "back": axis = FilletAxis.Y; canonical = "front"; isAlias = true; return true;
        case "right": axis = FilletAxis.X; canonical = "right"; return true;
        case "x": axis = FilletAxis.X; canonical = "right"; return true;
        case "left":
        case "side": axis = FilletAxis.X; canonical = "right"; isAlias = true; return true;
        default: axis = FilletAxis.Extrusion; canonical = null; return false;
      }
    }

    // ---- Single values ---------------------------------------------------------------

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

    /// <summary>
    /// Reads a number, a number with a unit, or arithmetic. An unreadable value is an
    /// error and marks the part, so the runner skips it instead of building it wrong.
    /// </summary>
    static Measure? ParseMeasure(Part part, string value, string name, int line, List<Diagnostic> diagnostics)
    {
      if (value.Length == 0)
        return null;
      if (!Units.TryParse(value, out var measure, out var unknownUnit, out var error))
      {
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, line,
          error == null ? $"Cannot read {name} '{value}'." : $"Cannot read {name} '{value}': {error}."));
        part.HasError = true;
        return null;
      }
      if (unknownUnit != null)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line, $"Unknown unit '{unknownUnit}' on {name}; using model units."));
      return measure;
    }

    static void ApplyPosition(Part part, string value, int line, List<Diagnostic> diagnostics)
    {
      var inner = StripEnclosing(value);
      var parts = inner.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length < 2)
        parts = inner.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

      if (parts.Length < 2)
      {
        // Resolvable to a default, so this warns and the part is still built, the same
        // way an unreadable color or orientation does.
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
          $"Position needs two values, got '{value}'; using 0, 0."));
        return;
      }

      var a = ParseMeasure(part, parts[0].Trim(), "position", line, diagnostics);
      var b = ParseMeasure(part, parts[1].Trim(), "position", line, diagnostics);
      if (!a.HasValue || !b.HasValue)
        return;

      part.PositionA = a.Value;
      part.PositionB = b.Value;

      if (parts.Length > 2)
      {
        var c = ParseMeasure(part, parts[2].Trim(), "offset", line, diagnostics);
        if (c.HasValue)
          part.Offset = c.Value;
      }
    }

    /// <summary>
    /// Removes brackets around a whole list ("[1, 2]", "(3, 4)"). Parentheses are only
    /// removed when they wrap the entire value, so "(1 + 1), 2" keeps its arithmetic.
    /// </summary>
    static string StripEnclosing(string value)
    {
      var text = (value ?? string.Empty).Trim().TrimStart('[').TrimEnd(']').Trim();
      if (text.Length >= 2 && text[0] == '(' && text[text.Length - 1] == ')')
      {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
          if (text[i] == '(') depth++;
          else if (text[i] == ')') depth--;
          if (depth == 0 && i < text.Length - 1)
            return text;
        }
        return text.Substring(1, text.Length - 2).Trim();
      }
      return text;
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
          case "pipe":
          case "tube":
            part.Actions.Add(PartAction.Pipe);
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

      // The value is a sequence of at most two words: how the geometry is made, then what
      // the solid does. Anything beyond that resolves to the first word of each kind.
      var generators = part.Actions.Where(IsGeneratorAction).Distinct().ToList();
      if (generators.Count > 1)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
          $"Actions '{value}' name more than one way to build the part; using '{ActionWord(generators[0])}'."));

      var roles = part.Actions.Where(a => !IsGeneratorAction(a)).Distinct().ToList();
      if (roles.Count > 1)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
          $"Actions '{value}' name more than one role for the solid; using '{ActionWord(roles[0])}'."));

      if (part.Generator == PartGenerator.Curve && roles.Count > 0)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, line,
          $"A curve cannot {ActionWord(roles[0])}; the curve is kept as it is."));
    }

    static bool IsGeneratorAction(PartAction action)
      => action == PartAction.Extrude || action == PartAction.Curve || action == PartAction.Pipe;

    static string ActionWord(PartAction action) => action.ToString().ToLowerInvariant();

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

    // ---- Document checks -------------------------------------------------------------

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
          ValidatePart(part, doc.Diagnostics);
      }
    }

    static void ValidatePart(Part part, List<Diagnostic> diagnostics)
    {
      if (part.Actions.Count == 0)
        part.Actions.Add(PartAction.Extrude);

      var generator = part.Generator;
      var missing = new List<string>();
      bool nonPositive;

      if (generator == PartGenerator.Pipe)
      {
        // A pipe is its curve and its radius; dimensions and position do not apply.
        if (string.IsNullOrWhiteSpace(part.CurveName)) missing.Add("curve");
        if (part.RadiusStations.Count == 0) missing.Add("radius");
        nonPositive = part.RadiusStations.Any(s => s.Radius.Value <= 0);
      }
      else
      {
        if (part.RadiusStations.Any(s => s.Parameter.HasValue))
        {
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, part.Line,
            $"Part '{part.DisplayName}': radius stations only apply to pipes; the first radius is used."));
          if (!part.Width.HasValue && !part.Height.HasValue)
          {
            part.Width = part.RadiusStations[0].Radius;
            part.Height = part.RadiusStations[0].Radius;
            if (!part.ShapeSet)
              part.Shape = PartShape.Circle;
          }
        }

        if (!part.Height.HasValue) missing.Add("height");
        if (!part.Width.HasValue) missing.Add("width");
        // Only an extruded part needs a depth. Testing "has a curve action" here instead
        // let "extrude, curve" through validation and the runner then dropped the part.
        if (!part.Depth.HasValue && generator == PartGenerator.Extrude) missing.Add("depth");

        nonPositive = part.Height.HasValue && part.Height.Value.Value <= 0 ||
                      part.Width.HasValue && part.Width.Value.Value <= 0 ||
                      part.Depth.HasValue && part.Depth.Value.Value <= 0;
      }

      if (missing.Count > 0)
      {
        part.HasError = true;
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
          $"Part '{part.DisplayName}' is missing: {string.Join(", ", missing)}."));
      }

      if (nonPositive)
      {
        part.HasError = true;
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
          $"Part '{part.DisplayName}' has a zero or negative dimension."));
      }

      if (part.Fillet != null && generator != PartGenerator.Extrude)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, part.Line,
          $"Part '{part.DisplayName}': filletEdge applies to extruded parts only; it is ignored."));

      if (part.CurveName != null && generator != PartGenerator.Pipe)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, part.Line,
          $"Part '{part.DisplayName}': 'curve' is only read by pipe parts."));
    }

    static string Truncate(string text, int max)
      => text.Length <= max ? text : text.Substring(0, max) + "...";
  }
}
