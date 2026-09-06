using System;
using System.Collections.Generic;
using System.Linq;

namespace ListModeling.Core.Syntax
{
  /// <summary>Which face of the model a part is drawn on. Decides the axis mapping.</summary>
  public enum Orientation { Top, Bottom, Front, Back, Right, Left }

  /// <summary>Profile drawn before the depth is applied.</summary>
  public enum PartShape { Rectangle, Circle, Ellipse, Polygon }

  /// <summary>What the runner does with a part's profile.</summary>
  public enum PartAction { Extrude, Subtract, Union, Curve }

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

    public PartAction PrimaryAction => Actions.Count > 0 ? Actions[0] : PartAction.Extrude;

    public bool HasAction(PartAction action) => Actions.Contains(action);

    public bool HasAllDimensions => Height.HasValue && Width.HasValue && Depth.HasValue;

    public string DisplayName => string.IsNullOrEmpty(Group) ? Name : $"{Name} [group:{Group}]";
  }
}
