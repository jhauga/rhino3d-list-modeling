using System;
using System.Collections.Generic;
using System.Linq;

namespace ListModeling.Core.Syntax
{
  /// <summary>Which face of the model a part is drawn on. Decides the axis mapping.</summary>
  public enum Orientation { Top, Bottom, Front, Back, Right, Left }

  /// <summary>Profile drawn before the depth is applied.</summary>
  public enum PartShape { Rectangle, Circle, Ellipse, Polygon }

  /// <summary>
  /// One word of a part's "actions" value. Extrude, Curve and Pipe say how the geometry
  /// is made; Subtract and Union say what the resulting solid does in its element.
  /// </summary>
  public enum PartAction { Extrude, Subtract, Union, Curve, Pipe }

  /// <summary>How a part makes its geometry: the first slot of its actions.</summary>
  public enum PartGenerator { Extrude, Curve, Pipe }

  /// <summary>What a part's solid does in its element: the second slot of its actions.</summary>
  public enum PartRole { Keep, Subtract, Union }

  /// <summary>
  /// The direction fillet loops are counted along. Extrusion follows the part's own depth
  /// axis (0 is the profile plane, 1 the far end); X, Y and Z count from the lowest
  /// coordinate on that world axis.
  /// </summary>
  public enum FilletAxis { Extrusion, X, Y, Z }

  /// <summary>Length unit written after a number. Model means "use the document's units".</summary>
  public enum LengthUnit { Model, Millimeters, Centimeters, Meters, Inches, Feet }

  public enum DiagnosticLevel { Info, Warning, Error }

  /// <summary>A number with an optional unit, exactly as written in the list.</summary>
  public readonly struct Measure
  {
    public Measure(double value, LengthUnit unit)
    {
      Value = value;
      Unit = unit;
    }

    public double Value { get; }
    public LengthUnit Unit { get; }

    public override string ToString() => Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + Units.Suffix(Unit);
  }

  /// <summary>A parse or run message tied to a source line (1-based; 0 when not line specific).</summary>
  public sealed class Diagnostic
  {
    public Diagnostic(DiagnosticLevel level, int line, string message)
    {
      Level = level;
      Line = line;
      Message = message;
    }

    public DiagnosticLevel Level { get; }
    public int Line { get; }
    public string Message { get; }

    public override string ToString()
      => Line > 0 ? $"{Level} (line {Line}): {Message}" : $"{Level}: {Message}";
  }

  /// <summary>The whole list: a sequence of elements plus what the parser had to say about it.</summary>
  public sealed class ListDocument
  {
    public List<Element> Elements { get; } = new List<Element>();
    public List<Diagnostic> Diagnostics { get; } = new List<Diagnostic>();

    public bool HasErrors => Diagnostics.Any(d => d.Level == DiagnosticLevel.Error);

    public IEnumerable<Part> AllParts => Elements.SelectMany(e => e.Parts);
  }

  /// <summary>
  /// A level 1 header: the operation applied across its parts. Type is the word after
  /// "Element:" (Boolean, BooleanDifference, BooleanUnion, Loft, ...), kept as written.
  /// </summary>
  public sealed class Element
  {
    public Element(string kind, string type, int line)
    {
      Kind = string.IsNullOrWhiteSpace(kind) ? "Element" : kind;
      Type = string.IsNullOrWhiteSpace(type) ? "Boolean" : type;
      Line = line;
    }

    /// <summary>"Element" or "Part"; only informational.</summary>
    public string Kind { get; }
    public string Type { get; set; }
    public int Line { get; }
    public List<Part> Parts { get; } = new List<Part>();

    public ElementFamily Family => ElementTypes.FamilyOf(Type);
  }

  /// <summary>How the runner treats an element type.</summary>
  public enum ElementFamily
  {
    /// <summary>Boolean, BooleanDifference, Solid: solids per part, cutters subtract.</summary>
    Solid,
    /// <summary>BooleanUnion: all extruded parts are unioned first.</summary>
    Union,
    /// <summary>Loft, Sweep1, Sweep2, Revolve, Patch, Cap: planned; profiles only for now.</summary>
    Surface,
    Unknown,
  }

  public static class ElementTypes
  {
    public static readonly string[] Solid = { "Boolean", "BooleanDifference", "Difference", "Solid" };
    public static readonly string[] Union = { "BooleanUnion", "Union" };
    public static readonly string[] Surface = { "Loft", "Sweep1", "Sweep2", "Revolve", "Patch", "Cap", "Surface" };

    public static ElementFamily FamilyOf(string type)
    {
      if (string.IsNullOrWhiteSpace(type))
        return ElementFamily.Solid;
      var key = type.Replace(" ", "").Replace("_", "").Replace("-", "");
      if (Solid.Any(s => s.Equals(key, StringComparison.OrdinalIgnoreCase)))
        return ElementFamily.Solid;
      if (Union.Any(s => s.Equals(key, StringComparison.OrdinalIgnoreCase)))
        return ElementFamily.Union;
      if (Surface.Any(s => s.Equals(key, StringComparison.OrdinalIgnoreCase)))
        return ElementFamily.Surface;
      return ElementFamily.Unknown;
    }
  }

  /// <summary>
  /// One point of a pipe's radius. Parameter is null for a constant radius; otherwise it
  /// is a parameter on the rail curve, or a percentage of the rail's length when IsPercent.
  /// </summary>
  public readonly struct RadiusStation
  {
    public RadiusStation(double? parameter, bool isPercent, Measure radius)
    {
      Parameter = parameter;
      IsPercent = isPercent;
      Radius = radius;
    }

    public double? Parameter { get; }
    public bool IsPercent { get; }
    public Measure Radius { get; }
  }

  /// <summary>
  /// One "select" line of a filletEdge block with the radius that goes with it.
  /// Loops lists the selected loop indices (0 or 1) along Axis. Radii has one entry per
  /// loop, in the same order: one value fillets the whole loop, several values go edge by
  /// edge. Fewer entries than loops means the last entry repeats.
  /// </summary>
  public sealed class FilletSelection
  {
    public FilletSelection(FilletAxis axis, int line)
    {
      Axis = axis;
      Line = line;
    }

    public FilletAxis Axis { get; }
    public int Line { get; }
    public List<int> Loops { get; } = new List<int>();
    public List<List<Measure>> Radii { get; } = new List<List<Measure>>();

    public FilletSelection Clone()
    {
      var copy = new FilletSelection(Axis, Line);
      copy.Loops.AddRange(Loops);
      foreach (var entry in Radii)
        copy.Radii.Add(new List<Measure>(entry));
      return copy;
    }
  }

  /// <summary>
  /// A part's filletEdge block: selections in the order written. When two selections name
  /// the same edge, the first one wins.
  /// </summary>
  public sealed class FilletSpec
  {
    public List<FilletSelection> Selections { get; } = new List<FilletSelection>();

    public FilletSpec Clone()
    {
      var copy = new FilletSpec();
      foreach (var selection in Selections)
        copy.Selections.Add(selection.Clone());
      return copy;
    }
  }

  /// <summary>
  /// One modeled piece: a level 2 header, or a level 3 "group:" header nested under one.
  /// Every dimension is optional at parse time; the runner reports what is missing.
  /// </summary>
  public sealed class Part
  {
    public Part(string name, int line)
    {
      Name = string.IsNullOrWhiteSpace(name) ? "Part" : name.Trim();
      Line = line;
    }

    public string Name { get; set; }
    public int Line { get; }

    /// <summary>Group identifier from a "### group:id" header; null when not grouped.</summary>
    public string Group { get; set; }

    /// <summary>Layer name, or a nested layer path joined with "::" (Rhino's separator).</summary>
    public string LayerName { get; set; }
    public string LayerColor { get; set; }
    public Orientation Orientation { get; set; } = Orientation.Top;
    public PartShape Shape { get; set; } = PartShape.Rectangle;
    /// <summary>True when the list named the shape; a bare "radius" then does not imply a circle.</summary>
    public bool ShapeSet { get; set; }
    public int PolygonSides { get; set; } = 6;
    public Measure? Height { get; set; }
    public Measure? Width { get; set; }
    public Measure? Depth { get; set; }

    /// <summary>First plane coordinate (x for top/bottom/front/back, y for left/right).</summary>
    public Measure PositionA { get; set; } = new Measure(0, LengthUnit.Model);
    /// <summary>Second plane coordinate (y for top/bottom, z for the others).</summary>
    public Measure PositionB { get; set; } = new Measure(0, LengthUnit.Model);
    /// <summary>Optional start along the depth axis; the profile is lifted here before extruding.</summary>
    public Measure Offset { get; set; } = new Measure(0, LengthUnit.Model);

    /// <summary>Name of the curve a pipe follows: a curve part in the same list, or a named curve in the model.</summary>
    public string CurveName { get; set; }

    /// <summary>
    /// Every radius the list gave this part. A plain "radius: 2" is one station with no
    /// parameter and also sets Width and Height; a nested block gives one station per line.
    /// </summary>
    public List<RadiusStation> RadiusStations { get; } = new List<RadiusStation>();

    /// <summary>The filletEdge block; null when the part has none.</summary>
    public FilletSpec Fillet { get; set; }

    /// <summary>Action words as written, unknown words left out.</summary>
    public List<PartAction> Actions { get; } = new List<PartAction>();

    /// <summary>Keys the parser did not recognize, kept so the writer can round-trip them.</summary>
    public Dictionary<string, string> Extra { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True once any property line has been applied to this part.</summary>
    public bool HasProperties { get; set; }

    /// <summary>
    /// True when a deeper header appeared under this one, which makes this header a
    /// label for the parts nested below rather than a part of its own. Whatever it
    /// declares becomes the defaults those children start from.
    /// </summary>
    public bool IsContainer { get; set; }

    /// <summary>
    /// True when this part carries an error diagnostic, which means it cannot be built
    /// correctly and the runner skips it. Values the parser could resolve to a default
    /// produce a warning instead and the part is still built.
    /// </summary>
    public bool HasError { get; set; }

    /// <summary>The first of extrude, curve or pipe in the actions; extrude when none is named.</summary>
    public PartGenerator Generator
    {
      get
      {
        foreach (var action in Actions)
        {
          switch (action)
          {
            case PartAction.Extrude: return PartGenerator.Extrude;
            case PartAction.Curve: return PartGenerator.Curve;
            case PartAction.Pipe: return PartGenerator.Pipe;
          }
        }
        return PartGenerator.Extrude;
      }
    }

    /// <summary>The first of subtract or union in the actions; keep when none is named. A curve always keeps.</summary>
    public PartRole Role
    {
      get
      {
        if (Generator == PartGenerator.Curve)
          return PartRole.Keep;
        foreach (var action in Actions)
        {
          if (action == PartAction.Subtract)
            return PartRole.Subtract;
          if (action == PartAction.Union)
            return PartRole.Union;
        }
        return PartRole.Keep;
      }
    }

    /// <summary>
    /// One word summing up the actions: curve or pipe for those generators, otherwise the
    /// role of the extruded solid (subtract, union), or extrude when it is simply kept.
    /// </summary>
    public PartAction PrimaryAction
    {
      get
      {
        switch (Generator)
        {
          case PartGenerator.Curve: return PartAction.Curve;
          case PartGenerator.Pipe: return PartAction.Pipe;
        }
        switch (Role)
        {
          case PartRole.Subtract: return PartAction.Subtract;
          case PartRole.Union: return PartAction.Union;
          default: return PartAction.Extrude;
        }
      }
    }

    public bool HasAction(PartAction action) => Actions.Contains(action);

    public bool HasAllDimensions => Height.HasValue && Width.HasValue && Depth.HasValue;

    public string DisplayName => string.IsNullOrEmpty(Group) ? Name : $"{Name} [group:{Group}]";
  }
}
