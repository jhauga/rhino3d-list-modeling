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
  ///   - key: value             before the first header: a default for every part in the element
  ///   ## Name                  a header; deeper headers ("###", "####", ...) nest under it
  ///   ### group:id             a header whose part joins the group with that id
  ///   - key: value             a property of the header above it ("*" and "+" bullets also work)
  ///     - key: value           a nested line, read by the less indented bullet above it
  ///
  /// A header with deeper headers under it is a container: it is never built, and its
  /// lines are defaults for every header nested below it, at any depth. A header with
  /// none is a part. A part starts from the element defaults, then applies the lines of
  /// each header above it, outermost first, then its own, so the nearest line wins. An
  /// empty value drops what the headers above said about that key.
  ///
  /// A header that repeats an earlier name reuses it. A repeated container name continues
  /// that container: lines under the repeat apply to its parts from there on, and only the
  /// headers listed under the repeat are built. A repeat with nothing under it builds that
  /// part, or every part of that container, again. "group:id[*]" and "group:id[n]" build
  /// parts of an earlier group again. A rebuilt part keeps the values of the headers it was
  /// first written under; the headers around the repeat fill in only what those leave out.
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
    static readonly Regex GroupPattern = new Regex(@"^\s*group\s*:\s*(?<id>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex GroupReferencePattern = new Regex(@"^\s*group\s*:\s*(?<id>[^\[\]]+?)\s*\[(?<sel>[^\]]*)\]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex StationParameter =new Regex(@"^\s*(?<num>[-+]?(?:\d+\.?\d*|\.\d+))\s*(?<percent>%)?\s*$", RegexOptions.Compiled);

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

    /// <summary>
    /// A header where a part or a container was first written, with the lines written under
    /// it. A later header with the same name may continue it, so its lines can grow.
    /// </summary>
    sealed class Definition
    {
      public Definition(string name, string group, int line, Definition parent)
      {
        Name = name;
        Group = group;
        Line = line;
        Parent = parent;
      }

      /// <summary>The header text; for a grouped part, the name of the nearest header above it that is not a group.</summary>
      public string Name { get; }
      public string Group { get; }
      public int Line { get; }
      /// <summary>The definition this one was written under; null at the top of an element.</summary>
      public Definition Parent { get; }
      public List<Node> Lines { get; } = new List<Node>();
      public List<Definition> Children { get; } = new List<Definition>();
      /// <summary>True once a deeper header was written under it.</summary>
      public bool IsContainer { get; set; }

      /// <summary>This definition and every one it was written under, outermost first.</summary>
      public List<Definition> Chain()
      {
        var chain = new List<Definition>();
        for (var level = this; level != null; level = level.Parent)
          chain.Insert(0, level);
        return chain;
      }

      /// <summary>Every part written under this container, at any depth, in the order written.</summary>
      public IEnumerable<Definition> Parts()
      {
        foreach (var child in Children)
        {
          if (!child.IsContainer)
            yield return child;
          else
            foreach (var part in child.Parts())
              yield return part;
        }
      }
    }

    /// <summary>
    /// What a header does where it is written. Define writes a new definition, or rebuilds an
    /// earlier part when nothing is written under it. Continue goes on with an earlier
    /// container. GroupRef rebuilds parts of an earlier group.
    /// </summary>
    enum PlaceKind { Define, Continue, GroupRef }

    /// <summary>One header as it appears in the element being read.</summary>
    sealed class Place
    {
      Place(int level, int line, PlaceKind kind, string name)
      {
        Level = level;
        Line = line;
        Kind = kind;
        Scratch = new Part(name, line);
      }

      public static Place Define(int level, int line, Definition definition, Definition earlier)
        => new Place(level, line, PlaceKind.Define, definition.Name) { Definition = definition, Earlier = earlier };

      public static Place Continue(int level, int line, Definition definition)
        => new Place(level, line, PlaceKind.Continue, definition.Name) { Definition = definition };

      public static Place Reference(int level, int line, string group, string selector)
        => new Place(level, line, PlaceKind.GroupRef, group) { Group = group, Selector = selector };

      public int Level { get; }
      public int Line { get; }
      public PlaceKind Kind { get; }
      /// <summary>The definition the lines go to; null for a group reference.</summary>
      public Definition Definition { get; private set; }
      /// <summary>Define only: an earlier part of the same name, rebuilt when nothing is written under this header.</summary>
      public Definition Earlier { get; private set; }
      /// <summary>GroupRef only: the group id and the text between the brackets.</summary>
      public string Group { get; private set; }
      public string Selector { get; private set; }
      /// <summary>GroupRef only: lines written under the reference, applied after everything else.</summary>
      public List<Node> SiteLines { get; } = new List<Node>();
      /// <summary>Where this header's lines go.</summary>
      public List<Node> Target => Kind == PlaceKind.GroupRef ? SiteLines : Definition.Lines;
      public int LineCount { get; set; }
      /// <summary>Define only: true once the definition is registered, which happens when anything is written under it.</summary>
      public bool Committed { get; set; }
      /// <summary>Takes every line once as it is read, so each problem is reported once and in order.</summary>
      public Part Scratch { get; }
      /// <summary>True once a deeper header appears under this one, which makes it a container.</summary>
      public bool HasDeeper { get; set; }
    }

    /// <summary>A part an element will build, with where it came from, until the element ends.</summary>
    sealed class Candidate
    {
      public Candidate(Part part, Definition source, bool isImplicit, bool hasSiteLines)
      {
        Part = part;
        Source = source;
        IsImplicit = isImplicit;
        HasSiteLines = hasSiteLines;
      }

      public Part Part { get; }
      public Definition Source { get; }
      /// <summary>Built by repeating a container's name; yields to explicit group references.</summary>
      public bool IsImplicit { get; }
      public bool HasSiteLines { get; }
    }

    public static ListDocument Parse(string text)
    {
      var doc = new ListDocument();
      if (text == null)
        return doc;

      Element element = null;
      var elementDefaults = new List<Node>();
      Part elementScratch = null;
      // Headers of the current element that are still open, outermost first.
      var places = new List<Place>();
      // What the current element will build, and the groups it references explicitly.
      var candidates = new List<Candidate>();
      var referencedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      // Whole document: the latest definition by header name, and each group's parts in order.
      var byName = new Dictionary<string, Definition>(StringComparer.Ordinal);
      var groups = new Dictionary<string, List<Definition>>(StringComparer.OrdinalIgnoreCase);

      // Top level property lines wait here until the next line at their level shows that
      // nothing more is nested under them.
      var pending = new List<Node>();
      var open = new Stack<Node>();
      bool inHtmlComment = false;

      // A new definition joins the document once anything is written under it, so a bare
      // repeat of an earlier part rebuilds that part instead of shadowing it.
      void Commit(Place place)
      {
        if (place.Kind != PlaceKind.Define || place.Committed)
          return;
        place.Committed = true;
        var definition = place.Definition;
        definition.Parent?.Children.Add(definition);
        if (definition.Group == null)
          byName[NameKey(definition.Name)] = definition;
        else
        {
          if (!groups.TryGetValue(definition.Group, out var items))
            groups[definition.Group] = items = new List<Definition>();
          items.Add(definition);
        }
      }

      void ApplyPending()
      {
        if (pending.Count > 0)
        {
          var place = places.Count > 0 ? places[places.Count - 1] : null;
          var lines = place != null ? place.Target : elementDefaults;
          var scratch = place != null ? place.Scratch : elementScratch;
          foreach (var node in pending)
          {
            lines.Add(node);
            ApplyProperty(scratch, node, doc.Diagnostics, null);
          }
          if (place != null)
          {
            place.LineCount += pending.Count;
            Commit(place);
          }
        }
        pending.Clear();
        open.Clear();
      }

      int? GroupIndex(Definition definition)
        => definition.Group != null && groups.TryGetValue(definition.Group, out var items) ? items.IndexOf(definition) : (int?)null;

      void Build(Definition definition, Place place, bool isImplicit, int? groupIndex)
        => candidates.Add(new Candidate(
          Resolve(definition, place, places, elementDefaults, place.SiteLines, groupIndex),
          definition, isImplicit, place.LineCount > 0 && place.Kind == PlaceKind.GroupRef));

      void BuildReference(Place place)
      {
        bool all = place.Selector.Trim() == "*";
        int index = -1;
        if (!all && !(int.TryParse(place.Selector.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out index)))
        {
          doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, place.Line,
            $"Cannot read '[{place.Selector}]' after group '{place.Group}'; write [*] or one number, counting from 0."));
          return;
        }
        if (!groups.TryGetValue(place.Group, out var items) || items.Count == 0)
        {
          doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, place.Line,
            $"No group '{place.Group}' is written before line {place.Line}."));
          return;
        }
        if (!all && index >= items.Count)
        {
          doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, place.Line,
            $"Group '{place.Group}' has {items.Count} part{(items.Count == 1 ? "" : "s")} before line {place.Line}, counting from 0; [{index}] is not one of them."));
          return;
        }

        referencedGroups.Add(place.Group);
        if (all)
          for (int i = 0; i < items.Count; i++)
            Build(items[i], place, false, i);
        else
          Build(items[index], place, false, index);
      }

      // Closes the open headers at or below a level. A header that closes without a deeper
      // header under it builds now, from the lines read so far.
      void CloseFrom(int level)
      {
        while (places.Count > 0 && places[places.Count - 1].Level >= level)
        {
          var place = places[places.Count - 1];
          if (!place.HasDeeper)
          {
            switch (place.Kind)
            {
              case PlaceKind.GroupRef:
                BuildReference(place);
                break;

              case PlaceKind.Continue:
                if (place.LineCount > 0)
                  doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, place.Line,
                    $"Header '{place.Definition.Name}' (line {place.Line}) continues the container from line {place.Definition.Line}; its lines apply from here on and nothing is built from it here."));
                else
                  foreach (var part in place.Definition.Parts())
                    Build(part, place, true, GroupIndex(part));
                break;

              default:
                if (place.LineCount == 0 && place.Earlier != null)
                  Build(place.Earlier, place, false, GroupIndex(place.Earlier));
                else
                {
                  Commit(place);
                  Build(place.Definition, place, false, null);
                }
                break;
            }
          }
          places.RemoveAt(places.Count - 1);
        }
      }

      // Every header is closed. A rebuilt container leaves out the groups this element
      // references by name, and each definition is built once per element.
      void FinishElement()
      {
        CloseFrom(0);
        var first = new Dictionary<Definition, Candidate>();
        foreach (var candidate in candidates)
        {
          if (candidate.IsImplicit && candidate.Source.Group != null && referencedGroups.Contains(candidate.Source.Group))
            continue;
          if (!candidate.HasSiteLines)
          {
            if (first.TryGetValue(candidate.Source, out var earlier))
            {
              doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, candidate.Part.Line,
                $"'{candidate.Part.DisplayName}' is already built in this element (line {earlier.Part.Line}); the mention on line {candidate.Part.Line} is skipped."));
              continue;
            }
            first[candidate.Source] = candidate;
          }
          element.Parts.Add(candidate.Part);
        }
        candidates.Clear();
        referencedGroups.Clear();
      }

      void StartElement(Element started)
      {
        element = started;
        doc.Elements.Add(element);
        elementDefaults = new List<Node>();
        elementScratch = new Part(element.Type, element.Line);
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
            if (element != null)
              FinishElement();
            StartElement(ParseElementHeader(headerText, lineNumber));
            continue;
          }

          if (element == null)
          {
            StartElement(new Element("Element", "Boolean", lineNumber));
            doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, lineNumber,
              "No '# Element:' header before the first part; assuming '# Element: Boolean'."));
          }

          // A header belongs under the nearest open header with fewer "#", which becomes
          // a container for it. A group reference cannot hold headers.
          CloseFrom(level);
          while (places.Count > 0 && places[places.Count - 1].Kind == PlaceKind.GroupRef)
          {
            var reference = places[places.Count - 1];
            doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, lineNumber,
              $"Header '{headerText}' is nested under the group reference on line {reference.Line}, which cannot hold headers; it is read as if written beside it."));
            CloseFrom(reference.Level);
          }
          var above = places.Count > 0 ? places[places.Count - 1] : null;
          if (above != null)
          {
            above.HasDeeper = true;
            above.Definition.IsContainer = true;
            Commit(above);
          }

          var parent = above?.Definition;
          if (TryParseGroupHeader(headerText, out var groupId, out var selector))
          {
            places.Add(selector != null
              ? Place.Reference(level, lineNumber, groupId, selector)
              : Place.Define(level, lineNumber, new Definition(NearestName(parent) ?? headerText, groupId, lineNumber, parent), null));
            continue;
          }

          // A name written before continues that container, or rebuilds that part when
          // nothing is written under it. A container that is still open is never continued.
          byName.TryGetValue(NameKey(headerText), out var earlier);
          if (earlier != null && places.Any(p => p.Definition == earlier))
            earlier = null;
          places.Add(earlier != null && earlier.IsContainer
            ? Place.Continue(level, lineNumber, earlier)
            : Place.Define(level, lineNumber, new Definition(headerText, null, lineNumber, parent), earlier));
          continue;
        }

        var bullet = BulletPattern.Match(line);
        if (bullet.Success)
        {
          var node = ReadNode(IndentWidth(bullet.Groups["indent"].Value), lineNumber, bullet.Groups["body"].Value);

          if (element == null)
          {
            if (IsPropertyKey(node.Key))
              doc.Diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, lineNumber,
                "Property before any '# Element:' header is ignored."));
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
      if (element != null)
        FinishElement();

      Validate(doc);
      return doc;
    }

    /// <summary>How header names are compared: trimmed, case insensitive, runs of spaces as one.</summary>
    static string NameKey(string name)
      => Regex.Replace((name ?? string.Empty).Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>The name a grouped part takes: that of the nearest header above it that is not a group.</summary>
    static string NearestName(Definition definition)
    {
      for (var level = definition; level != null; level = level.Parent)
        if (level.Group == null)
          return level.Name;
      return null;
    }

    /// <summary>
    /// Builds a part by replaying lines. First the context: the element defaults, then the
    /// headers above the place it is built that it does not descend from. Then its chain:
    /// the headers it was written under, outermost first, with their lines as they stand
    /// now, and last the lines written under a group reference. An empty value in the
    /// chain falls back to what the context said. Problems were reported when the lines
    /// were read, so the replay only records whether the part has an error.
    /// </summary>
    static Part Resolve(Definition definition, Place place, List<Place> openPlaces, List<Node> elementDefaults,
      List<Node> siteLines, int? groupIndex)
    {
      var part = new Part(definition.Name, place.Line) { Group = definition.Group, GroupIndex = groupIndex };
      var muted = new List<Diagnostic>();
      var chain = definition.Chain();

      foreach (var node in elementDefaults)
        ApplyProperty(part, node, muted, null);
      foreach (var above in openPlaces)
      {
        if (above == place)
          break;
        if (above.Definition != null && !chain.Contains(above.Definition))
          foreach (var node in above.Definition.Lines)
            ApplyProperty(part, node, muted, null);
      }

      var baseline = part.Clone();
      foreach (var level in chain)
        foreach (var node in level.Lines)
          ApplyProperty(part, node, muted, baseline);
      foreach (var node in siteLines)
        ApplyProperty(part, node, muted, baseline);

      ResolveCoordinates(part);
      return part;
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

    /// <summary>
    /// "group:id" starts a part of that group; "group:id[*]" and "group:id[2]" refer back to
    /// parts of it written earlier. The selector is the text between the brackets, or null.
    /// </summary>
    static bool TryParseGroupHeader(string headerText, out string id, out string selector)
    {
      id = null;
      selector = null;
      var reference = GroupReferencePattern.Match(headerText);
      if (reference.Success)
      {
        id = reference.Groups["id"].Value.Trim();
        selector = reference.Groups["sel"].Value;
        return true;
      }
      var match = GroupPattern.Match(headerText);
      if (!match.Success)
        return false;
      id = match.Groups["id"].Value.Trim();
      return true;
    }

    // ---- Properties ---------------------------------------------------------------

    /// <summary>
    /// Applies one property line. An empty value with nothing nested under it puts that
    /// key back to what the baseline says: the built-in default when baseline is null.
    /// </summary>
    static void ApplyProperty(Part part, Node node, List<Diagnostic> diagnostics, Part baseline)
    {
      part.HasProperties = true;
      var key = NormalizeKey(node.Key);
      var value = node.Value;
      int line = node.Line;

      if (value.Length == 0 && node.Children.Count == 0 && ResetProperty(part, key, baseline))
        return;

      switch (key)
      {
        case "layername":
        case "layer":
          part.LayerName = ReadLayerPath(part, node, diagnostics, baseline);
          return;

        case "radius":
        case "r":
          ApplyRadius(part, node, diagnostics, baseline);
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
          if (value.Equals("through", StringComparison.OrdinalIgnoreCase))
          {
            part.Depth = null;
            part.DepthThrough = true;
          }
          else
          {
            part.Depth = ParseMeasure(part, value, "depth", line, diagnostics);
            part.DepthThrough = false;
          }
          break;
        case "offset":
        case "elevation":
          var offset = ParseMeasure(part, value, "offset", line, diagnostics);
          if (offset.HasValue)
            part.Coordinates.Add(new CoordinateWrite(CoordinateSource.Offset, CoordinateSlot.Depth, offset.Value));
          break;

        case "position":
        case "pos":
        case "center":
        case "centre":
          ApplyPosition(part, value, line, diagnostics);
          break;

        case "xposition":
        case "xpos":
        case "yposition":
        case "ypos":
        case "zposition":
        case "zpos":
          var world = ParseMeasure(part, value, key[0] + "-position", line, diagnostics);
          if (world.HasValue)
            part.Coordinates.Add(WorldWrite(key[0], world.Value));
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
      ApplyChildrenAsProperties(part, node.Children, diagnostics, baseline);
    }

    static void ApplyChildrenAsProperties(Part part, IEnumerable<Node> children, List<Diagnostic> diagnostics, Part baseline)
    {
      foreach (var child in children)
      {
        if (IsPropertyKey(child.Key))
          ApplyProperty(part, child, diagnostics, baseline);
        else
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, child.Line, "Ignored: " + Truncate(DescribeNode(child), 60)));
      }
    }

    /// <summary>
    /// Puts a known key back to the baseline's value (a fresh part when baseline is null).
    /// Returns false for an unknown key, which is kept as written instead.
    /// </summary>
    static bool ResetProperty(Part part, string key, Part baseline)
    {
      var from = baseline ?? new Part(part.Name, part.Line);
      switch (key)
      {
        case "layername":
        case "layer":
          part.LayerName = from.LayerName;
          return true;
        case "layercolor":
        case "color":
        case "colour":
          part.LayerColor = from.LayerColor;
          return true;
        case "orientation":
          part.Orientation = from.Orientation;
          return true;
        case "shape":
        case "sides":
          part.Shape = from.Shape;
          part.ShapeSet = from.ShapeSet;
          part.PolygonSides = from.PolygonSides;
          return true;
        case "height":
          part.Height = from.Height;
          return true;
        case "width":
          part.Width = from.Width;
          return true;
        case "depth":
        case "thickness":
        case "distance":
        case "length":
          part.Depth = from.Depth;
          part.DepthThrough = from.DepthThrough;
          return true;
        case "offset":
        case "elevation":
          DropCoordinates(part, CoordinateSource.Offset, baseline);
          return true;
        case "position":
        case "pos":
        case "center":
        case "centre":
          DropCoordinates(part, CoordinateSource.Position, baseline);
          return true;
        case "xposition":
        case "xpos":
          DropCoordinates(part, CoordinateSource.XPosition, baseline);
          return true;
        case "yposition":
        case "ypos":
          DropCoordinates(part, CoordinateSource.YPosition, baseline);
          return true;
        case "zposition":
        case "zpos":
          DropCoordinates(part, CoordinateSource.ZPosition, baseline);
          return true;
        case "radius":
        case "r":
          part.Width = from.Width;
          part.Height = from.Height;
          part.RadiusStations.Clear();
          part.RadiusStations.AddRange(from.RadiusStations);
          if (!part.ShapeSet)
            part.Shape = from.Shape;
          return true;
        case "filletedge":
        case "filletedges":
        case "fillet":
        case "fillets":
          part.Fillet = from.Fillet?.Clone();
          return true;
        case "actions":
        case "action":
          part.Actions.Clear();
          part.Actions.AddRange(from.Actions);
          return true;
        case "curve":
        case "rail":
          part.CurveName = from.CurveName;
          return true;
        default:
          return false;
      }
    }

    /// <summary>
    /// Drops the coordinates one kind of line wrote after the baseline. The baseline's own
    /// entries are always the start of the log, because the context is applied first, so
    /// what the context wrote shows through.
    /// </summary>
    static void DropCoordinates(Part part, CoordinateSource source, Part baseline)
    {
      int keep = baseline?.Coordinates.Count ?? 0;
      for (int i = part.Coordinates.Count - 1; i >= keep; i--)
        if (part.Coordinates[i].Source == source)
          part.Coordinates.RemoveAt(i);
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
    static string ReadLayerPath(Part part, Node node, List<Diagnostic> diagnostics, Part baseline)
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
            ApplyChildrenAsProperties(part, new[] { child }, diagnostics, baseline);
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
    static void ApplyRadius(Part part, Node node, List<Diagnostic> diagnostics, Part baseline)
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
        ApplyChildrenAsProperties(part, node.Children, diagnostics, baseline);
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
        case "box":
          part.Shape = PartShape.Rectangle;
          return;
        case "square":
          part.Shape = PartShape.Square;
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

      part.Coordinates.Add(new CoordinateWrite(CoordinateSource.Position, CoordinateSlot.PlaneA, a.Value));
      part.Coordinates.Add(new CoordinateWrite(CoordinateSource.Position, CoordinateSlot.PlaneB, b.Value));

      if (parts.Length > 2)
      {
        var c = ParseMeasure(part, parts[2].Trim(), "offset", line, diagnostics);
        if (c.HasValue)
          part.Coordinates.Add(new CoordinateWrite(CoordinateSource.Position, CoordinateSlot.Depth, c.Value));
      }
    }

    /// <summary>"x-position: 3" and its y and z forms: one world coordinate, mapped once the orientation is known.</summary>
    static CoordinateWrite WorldWrite(char axis, Measure value)
    {
      switch (axis)
      {
        case 'x': return new CoordinateWrite(CoordinateSource.XPosition, CoordinateSlot.WorldX, value);
        case 'y': return new CoordinateWrite(CoordinateSource.YPosition, CoordinateSlot.WorldY, value);
        default: return new CoordinateWrite(CoordinateSource.ZPosition, CoordinateSlot.WorldZ, value);
      }
    }

    /// <summary>
    /// Turns the coordinate log into PositionA, PositionB and Offset. Each of the three takes
    /// the last line that wrote it, whether that line named it on the orientation plane or as
    /// a world coordinate, so the line nearest the part wins. Unwritten ones are 0.
    /// </summary>
    static void ResolveCoordinates(Part part)
    {
      var zero = new Measure(0, LengthUnit.Model);
      Measure a = zero, b = zero, offset = zero;
      foreach (var write in part.Coordinates)
      {
        var slot = write.Slot;
        var value = write.Value;
        if (slot == CoordinateSlot.WorldX || slot == CoordinateSlot.WorldY || slot == CoordinateSlot.WorldZ)
        {
          var axis = slot == CoordinateSlot.WorldX ? WorldAxis.X : slot == CoordinateSlot.WorldY ? WorldAxis.Y : WorldAxis.Z;
          slot = AxisMap.SlotFor(part.Orientation, axis, out var factor);
          if (factor < 0)
            value = new Measure(0 - value.Value, value.Unit);   // 0 - v keeps a zero from turning into -0
        }

        switch (slot)
        {
          case CoordinateSlot.PlaneA: a = value; break;
          case CoordinateSlot.PlaneB: b = value; break;
          default: offset = value; break;
        }
      }
      part.PositionA = a;
      part.PositionB = b;
      part.Offset = offset;
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
          case "select":
            part.Actions.Add(PartAction.Select);
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
      => action == PartAction.Extrude || action == PartAction.Curve || action == PartAction.Pipe || action == PartAction.Select;

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
          ValidatePart(part, element.Family, doc.Diagnostics);
      }
    }

    static void ValidatePart(Part part, ElementFamily family, List<Diagnostic> diagnostics)
    {
      if (part.Actions.Count == 0)
        part.Actions.Add(PartAction.Extrude);

      var generator = part.Generator;
      var missing = new List<string>();
      bool nonPositive = false;

      if (generator == PartGenerator.Select)
      {
        ValidateSelect(part, family, diagnostics);
      }
      else if (generator == PartGenerator.Pipe)
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

        if (part.Shape == PartShape.Square)
        {
          // A square has one side, so whichever of width and height is written is both.
          // When both are written and differ, the runner warns and uses the width.
          if (!part.Width.HasValue && part.Height.HasValue)
            part.Width = part.Height;
          if (!part.Height.HasValue && part.Width.HasValue)
            part.Height = part.Width;
          if (!part.Width.HasValue)
            missing.Add("width");
        }
        else
        {
          if (!part.Height.HasValue) missing.Add("height");
          if (!part.Width.HasValue) missing.Add("width");
        }
        // Only an extruded part needs a depth. Testing "has a curve action" here instead
        // let "extrude, curve" through validation and the runner then dropped the part.
        if (!part.Depth.HasValue && !part.DepthThrough && generator == PartGenerator.Extrude) missing.Add("depth");

        // A negative depth extrudes the other way; only a zero depth has no extent at all.
        nonPositive = part.Height.HasValue && part.Height.Value.Value <= 0 ||
                      part.Width.HasValue && part.Width.Value.Value <= 0 ||
                      part.Depth.HasValue && part.Depth.Value.Value == 0;
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

      if (part.DepthThrough && generator == PartGenerator.Extrude)
      {
        // "through" is measured from the solids a cutter cuts, so a part that is not a
        // cutter has no depth at all.
        if (part.Role != PartRole.Subtract)
        {
          part.HasError = true;
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
            $"Part '{part.DisplayName}': depth 'through' only applies to subtract parts."));
        }
        else if (part.Fillet != null)
          diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, part.Line,
            $"Part '{part.DisplayName}': filletEdge does nothing on a through cutter; it is ignored."));
      }

      if (part.Fillet != null && generator != PartGenerator.Extrude)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Warning, part.Line,
          $"Part '{part.DisplayName}': filletEdge applies to extruded parts only; it is ignored."));

      if (part.CurveName != null && generator != PartGenerator.Pipe)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, part.Line,
          $"Part '{part.DisplayName}': 'curve' is only read by pipe parts."));
    }

    /// <summary>
    /// A select part is the solids already on its layer, kept as the solids its element
    /// cuts. It needs the layer and nothing else; cutting or merging selected solids, and
    /// selecting in union or surface elements, are planned.
    /// </summary>
    static void ValidateSelect(Part part, ElementFamily family, List<Diagnostic> diagnostics)
    {
      if (string.IsNullOrWhiteSpace(part.LayerName))
      {
        part.HasError = true;
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
          $"Part '{part.DisplayName}': select needs a LayerName naming the layer to read."));
      }

      if (part.Role != PartRole.Keep)
      {
        part.HasError = true;
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
          $"Part '{part.DisplayName}': 'select, {part.Role.ToString().ToLowerInvariant()}' is planned; select reads kept solids only for now."));
      }

      if (family == ElementFamily.Union || family == ElementFamily.Surface)
      {
        part.HasError = true;
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Error, part.Line,
          $"Part '{part.DisplayName}': select works in Boolean and BooleanDifference elements only for now."));
      }

      if (part.Height.HasValue || part.Width.HasValue || part.Depth.HasValue || part.DepthThrough ||
          part.ShapeSet || part.Coordinates.Count > 0)
        diagnostics.Add(new Diagnostic(DiagnosticLevel.Info, part.Line,
          $"Part '{part.DisplayName}': select reads existing solids; its dimensions, shape and position are not used."));
    }

    static string Truncate(string text, int max)
      => text.Length <= max ? text : text.Substring(0, max) + "...";
  }
}
