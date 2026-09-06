using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using ListModeling.Core.Syntax;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ListModeling.Core.Runtime
{
  /// <summary>
  /// Turns a parsed list into document objects. One undo record per run.
  ///
  /// Per element:
  ///   extrude parts become solids, union parts are merged into them, subtract parts are
  ///   cut out of every solid, and curve parts add only their profile. Surface element
  ///   types (Loft, Revolve, ...) are planned; for now they produce the profile curves so
  ///   the surface step can be finished by hand.
  /// </summary>
  public sealed class ListRunner
  {
    readonly RhinoDoc _doc;
    readonly double _tolerance;

    public ListRunner(RhinoDoc doc)
    {
      _doc = doc ?? throw new ArgumentNullException(nameof(doc));
      _tolerance = doc.ModelAbsoluteTolerance;
    }

    public static RunReport RunFile(RhinoDoc doc, string path)
    {
      var name = Path.GetFileName(path ?? string.Empty);
      if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
      {
        var missing = new RunReport(name);
        missing.Errors.Add("List file not found: " + path);
        return missing;
      }

      string text;
      try
      {
        text = File.ReadAllText(path);
      }
      catch (Exception ex)
      {
        var failed = new RunReport(name);
        failed.Errors.Add("Cannot read list file: " + ex.Message);
        return failed;
      }

      return RunText(doc, text, name);
    }

    public static RunReport RunText(RhinoDoc doc, string text, string sourceName)
      => new ListRunner(doc).Run(ListParser.Parse(text), sourceName);

    public RunReport Run(ListDocument list, string sourceName)
    {
      var report = new RunReport(sourceName);
      report.AddDiagnostics(list.Diagnostics);

      if (list.Elements.Count == 0)
      {
        report.Errors.Add("No '# Element:' headers found; nothing to build.");
        return report;
      }

      uint undo = _doc.BeginUndoRecord("List Modeling: " + report.Source);
      var groups = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
      try
      {
        foreach (var element in list.Elements)
          RunElement(element, report, groups);

        ApplyGroups(groups, report);
      }
      catch (Exception ex)
      {
        report.Errors.Add("Unexpected failure: " + ex.Message);
      }
      finally
      {
        _doc.EndUndoRecord(undo);
        _doc.Views.Redraw();
      }

      return report;
    }

    // ---- Elements -----------------------------------------------------------

    sealed class Built
    {
      public Part Part;
      public Plane Plane;
      public Vector3d DepthDirection;
      public double Width, Height, Depth;
      public Curve Profile;
      public Brep Solid;
      public int LayerIndex;
    }

    void RunElement(Element element, RunReport report, Dictionary<string, List<Guid>> groups)
    {
      var family = element.Family;
      if (family == ElementFamily.Surface)
        report.Warnings.Add($"Element '{element.Type}' (line {element.Line}) is planned; profile curves are created so the surface can be finished manually.");

      var bases = new List<Built>();
      var unions = new List<Built>();
      var cutters = new List<Built>();

      foreach (var part in element.Parts)
      {
        var built = BuildPart(part, report);
        if (built == null)
          continue;

        if (family == ElementFamily.Surface || part.PrimaryAction == PartAction.Curve)
        {
          AddCurve(built, report, groups);
          continue;
        }

        if (built.Solid == null)
        {
          report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): could not build a closed solid.");
          continue;
        }

        switch (part.PrimaryAction)
        {
          case PartAction.Subtract: cutters.Add(built); break;
          case PartAction.Union: unions.Add(built); break;
          default: bases.Add(built); break;
        }
      }

      if (family == ElementFamily.Union)
      {
        unions.AddRange(bases);
        bases.Clear();
      }

      // Union step: everything marked union merges with the extruded solids.
      if (unions.Count > 0)
      {
        var pool = bases.Concat(unions).ToList();
        var merged = Brep.CreateBooleanUnion(pool.Select(b => b.Solid), _tolerance);
        if (merged == null || merged.Length == 0)
        {
          report.Warnings.Add($"Element '{element.Type}' (line {element.Line}): union failed; parts are kept separate.");
          bases.AddRange(unions);
        }
        else
        {
          var template = pool[0];
          bases.Clear();
          foreach (var brep in merged)
            bases.Add(CloneWithSolid(template, brep));
        }
      }

      // Subtract step: every cutter is taken out of every remaining solid.
      if (cutters.Count > 0)
      {
        if (bases.Count == 0)
        {
          report.Warnings.Add($"Element '{element.Type}' (line {element.Line}): subtract parts but nothing to subtract from; cutters are added as solids.");
          foreach (var cutter in cutters)
            AddSolid(cutter, cutter.Solid, report, groups, isBase: false);
        }
        else
        {
          var cutterBreps = cutters.Select(c => c.Solid).ToList();
          List<Brep> extended = null;
          bool manualFallback = false;

          foreach (var b in bases)
          {
            var result = Brep.CreateBooleanDifference(new[] { b.Solid }, cutterBreps, _tolerance);
            if (result == null || result.Length == 0)
            {
              // Coplanar caps make booleans flaky; retry with cutters that overshoot both ends.
              extended = extended ?? cutters.Select(c => ExtendedCutter(c)).Where(c => c != null).ToList();
              if (extended.Count == cutters.Count)
                result = Brep.CreateBooleanDifference(new[] { b.Solid }, extended, _tolerance);
            }

            if (result == null || result.Length == 0)
            {
              report.Warnings.Add($"Part '{b.Part.DisplayName}' (line {b.Part.Line}): boolean difference failed; solid and cutters are added for manual work.");
              AddSolid(b, b.Solid, report, groups, isBase: true);
              manualFallback = true;
              continue;
            }

            foreach (var brep in result)
              AddSolid(b, brep, report, groups, isBase: true);
          }

          if (manualFallback)
            foreach (var cutter in cutters)
              AddSolid(cutter, cutter.Solid, report, groups, isBase: false);
        }
      }
      else
      {
        foreach (var b in bases)
          AddSolid(b, b.Solid, report, groups, isBase: true);
      }
    }

    // ---- Parts --------------------------------------------------------------

    Built BuildPart(Part part, RunReport report)
    {
      // A part the parser flagged as an error cannot be built correctly: a negative
      // depth would extrude the wrong way, a missing one not at all. The error is
      // already in the report, so skip quietly rather than produce wrong geometry.
      if (part.HasError)
        return null;

      if (!part.Height.HasValue || !part.Width.HasValue)
        return null;

      bool needsDepth = part.PrimaryAction != PartAction.Curve;
      if (needsDepth && !part.Depth.HasValue)
        return null;

      double width = ToModel(part.Width.Value);
      double height = ToModel(part.Height.Value);
      double depth = part.Depth.HasValue ? ToModel(part.Depth.Value) : 0;
      double a = ToModel(part.PositionA);
      double b = ToModel(part.PositionB);
      double offset = ToModel(part.Offset);

      if (part.Shape == PartShape.Circle && Math.Abs(width - height) > _tolerance)
        report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): circle uses width as the radius; height differs and is ignored.");

      var plane = PartGeometry.ProfilePlane(part.Orientation, a, b, offset);
      PartGeometry.Frame(part.Orientation, out _, out _, out var depthDirection);

      var profile = PartGeometry.Profile(part.Shape, part.PolygonSides, plane, width, height);
      if (profile == null || !profile.IsValid)
      {
        report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): could not build the profile curve.");
        return null;
      }

      var built = new Built
      {
        Part = part,
        Plane = plane,
        DepthDirection = depthDirection,
        Width = width,
        Height = height,
        Depth = depth,
        Profile = profile,
        LayerIndex = ResolveLayer(part, report),
      };

      if (needsDepth)
        built.Solid = PartGeometry.Solid(profile, depthDirection * depth, _tolerance);

      return built;
    }

    /// <summary>The same cutter, started slightly before the profile plane and running slightly past the depth.</summary>
    Brep ExtendedCutter(Built cutter)
    {
      double margin = Math.Max(_tolerance * 10, cutter.Depth * 0.001);
      var profile = cutter.Profile.DuplicateCurve();
      profile.Translate(-cutter.DepthDirection * margin);
      return PartGeometry.Solid(profile, cutter.DepthDirection * (cutter.Depth + 2 * margin), _tolerance);
    }

    static Built CloneWithSolid(Built template, Brep solid)
      => new Built
      {
        Part = template.Part,
        Plane = template.Plane,
        DepthDirection = template.DepthDirection,
        Width = template.Width,
        Height = template.Height,
        Depth = template.Depth,
        Profile = template.Profile,
        Solid = solid,
        LayerIndex = template.LayerIndex,
      };

    // ---- Document -----------------------------------------------------------

    void AddSolid(Built built, Brep brep, RunReport report, Dictionary<string, List<Guid>> groups, bool isBase)
    {
      var id = _doc.Objects.AddBrep(brep, Attributes(built));
      if (id == Guid.Empty)
      {
        report.Errors.Add($"Part '{built.Part.DisplayName}' (line {built.Part.Line}): Rhino refused the solid.");
        return;
      }
      report.CreatedIds.Add(id);
      if (isBase)
        report.BaseIds.Add(id);
      Remember(groups, built.Part, id);
    }

    void AddCurve(Built built, RunReport report, Dictionary<string, List<Guid>> groups)
    {
      var id = _doc.Objects.AddCurve(built.Profile, Attributes(built));
      if (id == Guid.Empty)
      {
        report.Errors.Add($"Part '{built.Part.DisplayName}' (line {built.Part.Line}): Rhino refused the curve.");
        return;
      }
      report.CreatedIds.Add(id);
      Remember(groups, built.Part, id);
    }

    static ObjectAttributes Attributes(Built built)
      => new ObjectAttributes { LayerIndex = built.LayerIndex, Name = built.Part.Name };

    static void Remember(Dictionary<string, List<Guid>> groups, Part part, Guid id)
    {
      if (string.IsNullOrEmpty(part.Group))
        return;
      if (!groups.TryGetValue(part.Group, out var ids))
        groups[part.Group] = ids = new List<Guid>();
      ids.Add(id);
    }

    void ApplyGroups(Dictionary<string, List<Guid>> groups, RunReport report)
    {
      foreach (var pair in groups)
      {
        if (pair.Value.Count == 0)
          continue;
        var existing = _doc.Groups.FindName(pair.Key);
        int index = existing != null ? existing.Index : _doc.Groups.Add(pair.Key);
        if (index < 0)
        {
          report.Warnings.Add($"Could not create group '{pair.Key}'.");
          continue;
        }
        _doc.Groups.AddToGroup(index, pair.Value);
      }
    }

    /// <summary>
    /// Layer by name, created on demand with the part's color. A missing or empty name
    /// means the current layer. An unreadable color falls back to the layer default.
    /// </summary>
    int ResolveLayer(Part part, RunReport report)
    {
      if (string.IsNullOrWhiteSpace(part.LayerName))
        return _doc.Layers.CurrentLayerIndex;

      var name = part.LayerName.Trim();
      var found = _doc.Layers.FindName(name);
      if (found != null)
        return found.Index;

      var layer = new Layer { Name = name };
      if (!string.IsNullOrWhiteSpace(part.LayerColor))
      {
        if (ColorParser.TryParse(part.LayerColor, out Color color))
          layer.Color = color;
        else
          report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): color '{part.LayerColor}' not recognized; layer uses the default color.");
      }

      int index = _doc.Layers.Add(layer);
      if (index < 0)
      {
        report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): could not create layer '{name}'; using the current layer.");
        return _doc.Layers.CurrentLayerIndex;
      }
      return index;
    }

    // ---- Units --------------------------------------------------------------

    double ToModel(Measure measure)
    {
      if (measure.Unit == LengthUnit.Model)
        return measure.Value;
      return measure.Value * RhinoMath.UnitScale(ToUnitSystem(measure.Unit), _doc.ModelUnitSystem);
    }

    static UnitSystem ToUnitSystem(LengthUnit unit)
    {
      switch (unit)
      {
        case LengthUnit.Millimeters: return UnitSystem.Millimeters;
        case LengthUnit.Centimeters: return UnitSystem.Centimeters;
        case LengthUnit.Meters: return UnitSystem.Meters;
        case LengthUnit.Inches: return UnitSystem.Inches;
        case LengthUnit.Feet: return UnitSystem.Feet;
        default: return UnitSystem.None;
      }
    }
  }
}
