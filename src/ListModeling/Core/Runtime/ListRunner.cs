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
  /// Every curve part in the list is built first, so a pipe can follow a curve that is
  /// written anywhere in the list. Then, per element:
  ///   each part is made by its generator (an extruded profile, a profile curve, or a
  ///   pipe along a curve), fillets are applied to extruded solids while their edges
  ///   are still the ones the list describes, and each solid then plays its role: kept,
  ///   merged into the kept solids (union), or cut out of every one of them (subtract).
  ///   Surface element types (Loft, Revolve, ...) are planned; for now they produce the
  ///   profile curves so the surface step can be finished by hand.
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
        BuildCurvesFirst(list, report);
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
      /// <summary>True for a plain extrusion, which the boolean retry can rebuild slightly longer.</summary>
      public bool CanExtend;
      /// <summary>The curve a pipe follows, which the boolean retry can rebuild it around.</summary>
      public Curve Rail;
    }

    /// <summary>
    /// How far around a closed rail the boolean retry moves a pipe's seam: well away from
    /// the quarter points, where circles and cylinders put theirs.
    /// </summary>
    const double RetrySeamTurn = 0.37;

    /// <summary>Curve parts built ahead of their element, by part. A null value means the build failed.</summary>
    readonly Dictionary<Part, Built> _prebuilt = new Dictionary<Part, Built>();

    /// <summary>Profiles of curve parts by part name, for pipes to follow.</summary>
    readonly Dictionary<string, Curve> _rails = new Dictionary<string, Curve>(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when a part's output is its profile curve rather than a solid.</summary>
    static bool MakesCurve(Part part, ElementFamily family)
      => part.Generator == PartGenerator.Curve
      || (family == ElementFamily.Surface && part.Generator == PartGenerator.Extrude);

    /// <summary>
    /// Builds every curve part in the list before any element runs, so a pipe can name a
    /// curve that is written after it or in another element. The curves are still added
    /// to the document by their own element, in list order.
    /// </summary>
    void BuildCurvesFirst(ListDocument list, RunReport report)
    {
      foreach (var element in list.Elements)
      {
        foreach (var part in element.Parts)
        {
          if (!MakesCurve(part, element.Family) || _prebuilt.ContainsKey(part))
            continue;
          var built = BuildPart(part, element.Family, report);
          _prebuilt[part] = built;
          if (built?.Profile == null)
            continue;

          var key = part.Name.Trim();
          if (_rails.ContainsKey(key))
            report.Messages.Add($"Part '{part.DisplayName}' (line {part.Line}): another curve part is already named '{key}'; pipes follow the first one.");
          else
            _rails[key] = built.Profile;
        }
      }
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
        var built = _prebuilt.TryGetValue(part, out var early) ? early : BuildPart(part, family, report);
        if (built == null)
          continue;

        if (MakesCurve(part, family))
        {
          AddCurve(built, report, groups);
          continue;
        }

        if (built.Solid == null)
        {
          report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): could not build a closed solid.");
          continue;
        }

        switch (part.Role)
        {
          case PartRole.Subtract: cutters.Add(built); break;
          case PartRole.Union: unions.Add(built); break;
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
              // Coplanar caps and coinciding seams make booleans flaky. Retry with extruded
              // cutters that overshoot both ends and pipes whose seam is turned away; other
              // cutters take part in the retry as they are.
              extended = extended ?? cutters.Select(c => RetryCutter(c) ?? c.Solid).ToList();
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

    Built BuildPart(Part part, ElementFamily family, RunReport report)
    {
      // A part the parser flagged as an error cannot be built correctly: a negative
      // depth would extrude the wrong way, a missing one not at all. The error is
      // already in the report, so skip quietly rather than produce wrong geometry.
      if (part.HasError)
        return null;

      if (part.Generator == PartGenerator.Pipe)
        return BuildPipe(part, report);

      if (!part.Height.HasValue || !part.Width.HasValue)
        return null;

      bool needsDepth = !MakesCurve(part, family);
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
      {
        built.Solid = PartGeometry.Solid(profile, depthDirection * depth, _tolerance);
        built.CanExtend = built.Solid != null && part.Fillet == null;
        if (built.Solid != null && part.Fillet != null)
        {
          var frame = new FilletEdges.ExtrusionFrame(plane, depthDirection, depth);
          built.Solid = FilletEdges.Apply(built.Solid, part.Fillet, frame, ToModel, _tolerance, report, part.DisplayName);
        }
      }

      return built;
    }

    /// <summary>A pipe around a curve part from this list, or around a named curve already in the model.</summary>
    Built BuildPipe(Part part, RunReport report)
    {
      var name = (part.CurveName ?? string.Empty).Trim();
      var rail = FindRail(name);
      if (rail == null)
      {
        report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): no curve named '{name}' in the list or the model; the pipe is skipped.");
        return null;
      }

      var solid = PipeBuilder.Build(rail, part.RadiusStations, ToModel, _tolerance, _doc.ModelAngleToleranceRadians,
        out var problem, out var notice);
      if (solid == null)
      {
        report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): {problem}.");
        return null;
      }

      // A pipe about as thin as the model tolerance is built, but booleans with it fail
      // more often than not; say so up front rather than leave the failure unexplained.
      double thinnest = part.RadiusStations.Min(s => ToModel(s.Radius));
      if (thinnest < 5 * _tolerance)
        report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): a radius of {Invariant(thinnest)} is close to the model's absolute tolerance ({Invariant(_tolerance)}); booleans with this pipe may fail. A smaller tolerance in Document Properties helps.");

      if (notice != null)
      {
        if (part.Role != PartRole.Keep)
        {
          report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): {notice}, so it cannot {part.Role.ToString().ToLowerInvariant()}.");
          return null;
        }
        report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): {notice}; it is added as it is.");
      }

      return new Built
      {
        Part = part,
        Plane = Plane.WorldXY,
        DepthDirection = Vector3d.Zero,
        Solid = solid,
        LayerIndex = ResolveLayer(part, report),
        CanExtend = false,
        Rail = rail,
      };
    }

    Curve FindRail(string name)
    {
      if (name.Length == 0)
        return null;
      if (_rails.TryGetValue(name, out var listed))
        return listed;

      // A curve drawn by hand in the model, found by its object name.
      var settings = new ObjectEnumeratorSettings
      {
        ObjectTypeFilter = ObjectType.Curve,
        HiddenObjects = true,
        LockedObjects = true,
        DeletedObjects = false,
      };
      foreach (var obj in _doc.Objects.GetObjectList(settings))
      {
        if (string.Equals((obj.Attributes.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase) &&
            obj.Geometry is Curve curve)
          return curve.DuplicateCurve();
      }
      return null;
    }

    /// <summary>
    /// A second try at a cutter for a boolean that failed. A plain extrusion starts slightly
    /// before its profile plane and runs slightly past its depth; a pipe around a closed
    /// curve is rebuilt with its seam turned away from where it was. Null for any other
    /// cutter, which is retried as it is.
    /// </summary>
    Brep RetryCutter(Built cutter)
    {
      if (cutter.Rail != null && cutter.Rail.IsClosed)
        return PipeBuilder.Build(cutter.Rail, cutter.Part.RadiusStations, ToModel, _tolerance,
          _doc.ModelAngleToleranceRadians, out _, out _, RetrySeamTurn);

      if (!cutter.CanExtend || cutter.Profile == null)
        return null;
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
        CanExtend = false,
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
    /// means the current layer. A "Parent::Child" path finds or creates every level, and
    /// only the last level takes the color. An unreadable color falls back to the layer
    /// default.
    /// </summary>
    int ResolveLayer(Part part, RunReport report)
    {
      if (string.IsNullOrWhiteSpace(part.LayerName))
        return _doc.Layers.CurrentLayerIndex;

      var segments = part.LayerName.Split(new[] { ModelComponent.NamePathSeparator }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToArray();
      if (segments.Length == 0)
        return _doc.Layers.CurrentLayerIndex;

      if (segments.Length == 1)
      {
        // A plain name matches a layer of that name wherever it sits, as it always has.
        var found = _doc.Layers.FindName(segments[0]);
        if (found != null)
          return found.Index;
      }
      else
      {
        int existing = _doc.Layers.FindByFullPath(string.Join(ModelComponent.NamePathSeparator, segments), -1);
        if (existing >= 0)
          return existing;
      }

      Guid parentId = Guid.Empty;
      int index = -1;
      for (int level = 0; level < segments.Length; level++)
      {
        bool last = level == segments.Length - 1;
        var path = string.Join(ModelComponent.NamePathSeparator, segments.Take(level + 1));
        if (!(segments.Length == 1 && last))
        {
          int atLevel = _doc.Layers.FindByFullPath(path, -1);
          if (atLevel >= 0)
          {
            index = atLevel;
            parentId = _doc.Layers[atLevel].Id;
            continue;
          }
        }

        var layer = new Layer { Name = segments[level], ParentLayerId = parentId };
        if (last && !string.IsNullOrWhiteSpace(part.LayerColor))
        {
          if (ColorParser.TryParse(part.LayerColor, out Color color))
            layer.Color = color;
          else
            report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): color '{part.LayerColor}' not recognized; layer uses the default color.");
        }

        index = _doc.Layers.Add(layer);
        if (index < 0)
        {
          report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): could not create layer '{path}'; using the current layer.");
          return _doc.Layers.CurrentLayerIndex;
        }
        parentId = _doc.Layers[index].Id;
      }
      return index;
    }

    // ---- Units --------------------------------------------------------------

    static string Invariant(double value)
      => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

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
