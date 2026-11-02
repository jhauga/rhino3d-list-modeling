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
  ///   each part is made by its generator (an extruded profile, a profile curve, a pipe
  ///   along a curve, or the solids already on a layer for select), fillets are applied to
  ///   extruded solids while their edges are still the ones the list describes, and each
  ///   solid then plays its role: kept, merged into the kept solids (union), or cut out of
  ///   every kept solid it reaches (subtract). A "through" cutter is sized once the kept
  ///   solids are known. Selected solids are edited in place, keeping their object ids.
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
      /// <summary>A "depth: through" cutter: only the profile exists until the kept solids are known.</summary>
      public bool Through;
      /// <summary>Select only: the object this solid was read from, and its attributes.</summary>
      public Guid SourceId;
      public ObjectAttributes SourceAttributes;
      /// <summary>Select only: true once a union or a cut has changed the solid, so the object needs replacing.</summary>
      public bool Changed;
    }

    /// <summary>Selected objects already replaced in the element being run; later pieces of the same one are added beside it.</summary>
    readonly HashSet<Guid> _replaced = new HashSet<Guid>();

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
      var selected = new HashSet<Guid>();
      _replaced.Clear();

      foreach (var part in element.Parts)
      {
        if (part.Generator == PartGenerator.Select)
        {
          if (!part.HasError)
            bases.AddRange(SelectSolids(part, report, selected));
          continue;
        }

        var built = _prebuilt.TryGetValue(part, out var early) ? early : BuildPart(part, family, report);
        if (built == null)
          continue;

        if (MakesCurve(part, family))
        {
          AddCurve(built, report, groups);
          continue;
        }

        // A through cutter has only its profile until the kept solids are known.
        if (built.Through)
        {
          cutters.Add(built);
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

      if (unions.Count > 0 || cutters.Count > 0)
        foreach (var built in bases.Concat(unions).Concat(cutters))
          WarnIfThin(built, report);

      var consumed = new List<Guid>();
      if (unions.Count > 0)
        UnionStep(element, bases, unions, consumed, report);

      BuildThroughCutters(cutters, bases, report);

      if (cutters.Count == 0)
      {
        foreach (var b in bases)
          Keep(b, report, groups);
      }
      else if (bases.Count == 0)
      {
        report.Warnings.Add($"Element '{element.Type}' (line {element.Line}): subtract parts but nothing to subtract from; cutters are added as solids.");
        foreach (var cutter in cutters)
          AddSolid(cutter, cutter.Solid, report, groups, isBase: false);
      }
      else
      {
        SubtractStep(bases, cutters, report, groups);
      }

      // Selected objects that a union merged into another one are gone from the model now.
      foreach (var id in consumed)
        if (_doc.Objects.Delete(id, true))
          report.ModifiedIds.Add(id);
    }

    /// <summary>
    /// Everything marked union merges with the kept solids. Each merged piece belongs to the
    /// first selected solid that went into it (judged by bounding box), which it replaces;
    /// other selected solids merged into the same piece are consumed. A piece with no
    /// selected solid in it is a new object.
    /// </summary>
    void UnionStep(Element element, List<Built> bases, List<Built> unions, List<Guid> consumed, RunReport report)
    {
      var pool = bases.Concat(unions).ToList();
      var merged = Brep.CreateBooleanUnion(pool.Select(b => b.Solid), _tolerance);
      if (merged == null || merged.Length == 0)
      {
        report.Warnings.Add($"Element '{element.Type}' (line {element.Line}): union failed; parts are kept separate.");
        bases.AddRange(unions);
        return;
      }

      var sources = pool.Where(b => b.SourceId != Guid.Empty).ToList();
      var owners = new HashSet<Guid>();
      var template = pool.FirstOrDefault(b => b.SourceId == Guid.Empty) ?? pool[0];
      bases.Clear();
      foreach (var brep in merged)
      {
        var piece = brep.GetBoundingBox(true);
        piece.Inflate(_tolerance);
        var owner = sources.FirstOrDefault(s => !owners.Contains(s.SourceId) && piece.Contains(s.Solid.GetBoundingBox(true), false));
        Built built;
        if (owner != null)
        {
          owners.Add(owner.SourceId);
          built = CloneWithSolid(owner, brep);
        }
        else
        {
          built = CloneWithSolid(template, brep);
          built.SourceId = Guid.Empty;
          built.SourceAttributes = null;
        }
        built.Changed = true;
        bases.Add(built);
      }
      consumed.AddRange(sources.Where(s => !owners.Contains(s.SourceId)).Select(s => s.SourceId));
    }

    /// <summary>
    /// Each kept solid is cut by the cutters whose bounding boxes reach it; Rhino fails a
    /// difference outright when a cutter misses. A cutter that reaches none of them cut
    /// nothing, which usually means it is in the wrong place, so it stays in the model with
    /// a warning rather than disappearing.
    /// </summary>
    void SubtractStep(List<Built> bases, List<Built> cutters, RunReport report, Dictionary<string, List<Guid>> groups)
    {
      var used = new HashSet<Built>();
      bool manualFallback = false;

      foreach (var b in bases)
      {
        var reach = b.Solid.GetBoundingBox(true);
        reach.Inflate(_tolerance);
        var reaching = cutters.Where(c => Overlaps(reach, c.Solid.GetBoundingBox(true))).ToList();
        if (reaching.Count == 0)
        {
          Keep(b, report, groups);
          continue;
        }
        used.UnionWith(reaching);

        var result = Brep.CreateBooleanDifference(new[] { b.Solid }, reaching.Select(c => c.Solid), _tolerance);
        if (result == null || result.Length == 0)
        {
          // Coplanar caps and coinciding seams make booleans flaky. Retry with extruded
          // cutters that overshoot their ends outside this solid and pipes whose seam is
          // turned away; other cutters take part in the retry as they are.
          var retried = reaching.Select(c => RetryCutter(c, b.Solid) ?? c.Solid).ToList();
          result = Brep.CreateBooleanDifference(new[] { b.Solid }, retried, _tolerance);
        }

        if (result == null || result.Length == 0)
        {
          report.Warnings.Add($"Part '{b.Part.DisplayName}' (line {b.Part.Line}): boolean difference failed; solid and cutters are added for manual work.");
          Keep(b, report, groups);
          manualFallback = true;
          continue;
        }

        b.Changed = true;
        foreach (var brep in result)
          AddSolid(b, brep, report, groups, isBase: true);
      }

      foreach (var cutter in cutters)
      {
        if (used.Contains(cutter))
        {
          if (manualFallback)
            AddSolid(cutter, cutter.Solid, report, groups, isBase: false);
          continue;
        }
        report.Warnings.Add($"Part '{cutter.Part.DisplayName}' (line {cutter.Part.Line}): reaches none of the element's kept solids, so it cuts nothing; it is added for manual work.");
        AddSolid(cutter, cutter.Solid, report, groups, isBase: false);
      }
    }

    /// <summary>
    /// Sizes each "depth: through" cutter: its profile moves along its depth axis to just
    /// before the kept solids begin and extrudes to just past where they end.
    /// </summary>
    void BuildThroughCutters(List<Built> cutters, List<Built> bases, RunReport report)
    {
      foreach (var cutter in cutters.Where(c => c.Through).ToList())
      {
        if (bases.Count == 0)
        {
          report.Errors.Add($"Part '{cutter.Part.DisplayName}' (line {cutter.Part.Line}): depth 'through' has no kept solid to go through in this element; the part is skipped.");
          cutters.Remove(cutter);
          continue;
        }

        // Every orientation's depth axis is a world axis, so the extent is one interval.
        var axis = new Vector3d(Math.Abs(cutter.DepthDirection.X), Math.Abs(cutter.DepthDirection.Y), Math.Abs(cutter.DepthDirection.Z));
        double low = double.MaxValue, high = double.MinValue;
        foreach (var b in bases)
        {
          var box = b.Solid.GetBoundingBox(true);
          low = Math.Min(low, new Vector3d(box.Min) * axis);
          high = Math.Max(high, new Vector3d(box.Max) * axis);
        }
        double margin = Math.Max(10 * _tolerance, (high - low) * 0.01);

        var shift = axis * (low - margin - new Vector3d(cutter.Plane.Origin) * axis);
        var profile = cutter.Profile.DuplicateCurve();
        profile.Translate(shift);
        cutter.Plane = new Plane(cutter.Plane.Origin + shift, cutter.Plane.XAxis, cutter.Plane.YAxis);
        cutter.DepthDirection = axis;
        cutter.Depth = high - low + 2 * margin;
        cutter.Solid = PartGeometry.Solid(profile, axis * cutter.Depth, _tolerance);
        if (cutter.Solid == null)
        {
          report.Errors.Add($"Part '{cutter.Part.DisplayName}' (line {cutter.Part.Line}): could not build a closed solid.");
          cutters.Remove(cutter);
        }
      }
    }

    /// <summary>
    /// A solid thinner than about twice the model's absolute tolerance makes booleans fail,
    /// or worse, succeed with the wrong shape: a 0.004 pocket cut a board correctly at a
    /// tolerance of 0.001 and left it broken at 0.01. Pipes warn on their own and selected
    /// solids have no list dimensions, so neither is checked here.
    /// </summary>
    void WarnIfThin(Built built, RunReport report)
    {
      if (built.SourceId != Guid.Empty || built.Profile == null)
        return;
      double across;
      switch (built.Part.Shape)
      {
        case PartShape.Circle:
        case PartShape.Polygon: across = 2 * built.Width; break;
        case PartShape.Ellipse: across = 2 * Math.Min(built.Width, built.Height); break;
        case PartShape.Square: across = built.Width; break;
        default: across = Math.Min(built.Width, built.Height); break;
      }
      double thinnest = built.Through ? across : Math.Min(across, built.Depth);
      if (thinnest < 2 * _tolerance)
        report.Warnings.Add($"Part '{built.Part.DisplayName}' (line {built.Part.Line}): a size of {Invariant(thinnest)} is close to the model's absolute tolerance ({Invariant(_tolerance)}); booleans with this part may fail or come out wrong. A smaller tolerance in Document Properties helps.");
    }

    /// <summary>A kept solid with nothing more done to it. A selected one the element did not change is left alone.</summary>
    void Keep(Built built, RunReport report, Dictionary<string, List<Guid>> groups)
    {
      if (built.SourceId != Guid.Empty && !built.Changed)
        return;
      AddSolid(built, built.Solid, report, groups, isBase: true);
    }

    static bool Overlaps(BoundingBox a, BoundingBox b)
      => a.Min.X <= b.Max.X && b.Min.X <= a.Max.X
      && a.Min.Y <= b.Max.Y && b.Min.Y <= a.Max.Y
      && a.Min.Z <= b.Max.Z && b.Min.Z <= a.Max.Z;

    /// <summary>
    /// Every closed solid on exactly the part's layer that a person could select: visible,
    /// unlocked, on a visible, unlocked layer. Extrusions are read as breps. An object two
    /// select parts of one element both name is read once.
    /// </summary>
    List<Built> SelectSolids(Part part, RunReport report, HashSet<Guid> selected)
    {
      var solids = new List<Built>();
      int layerIndex = FindLayer(part.LayerName);
      if (layerIndex < 0)
      {
        report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): no layer '{part.LayerName}' in the model; nothing to select.");
        return solids;
      }

      var settings = new ObjectEnumeratorSettings
      {
        LayerIndexFilter = layerIndex,
        ObjectTypeFilter = ObjectType.Brep | ObjectType.Extrusion,
        NormalObjects = true,
        LockedObjects = false,
        HiddenObjects = false,
        DeletedObjects = false,
      };

      bool readBefore = false;
      foreach (var obj in _doc.Objects.GetObjectList(settings))
      {
        if (!obj.IsSelectable(true, true, false, false))
          continue;
        var brep = obj.Geometry is Extrusion extrusion ? extrusion.ToBrep(true) : (obj.Geometry as Brep)?.DuplicateBrep();
        if (brep == null || !brep.IsSolid)
          continue;
        if (!selected.Add(obj.Id))
        {
          readBefore = true;
          continue;
        }

        if (brep.SolidOrientation == BrepSolidOrientation.Inward)
          brep.Flip();
        solids.Add(new Built
        {
          Part = part,
          Plane = Plane.WorldXY,
          DepthDirection = Vector3d.Zero,
          Solid = brep,
          LayerIndex = obj.Attributes.LayerIndex,
          SourceId = obj.Id,
          SourceAttributes = obj.Attributes.Duplicate(),
        });
      }

      if (readBefore)
        report.Messages.Add($"Part '{part.DisplayName}' (line {part.Line}): solids another select part of this element already read are read once.");
      else if (solids.Count == 0)
        report.Errors.Add($"Part '{part.DisplayName}' (line {part.Line}): no visible, unlocked solid on layer '{part.LayerName}'; nothing to select.");
      return solids;
    }

    // ---- Parts --------------------------------------------------------------

    Built BuildPart(Part part, ElementFamily family, RunReport report)
    {
      // A part the parser flagged as an error cannot be built correctly: a zero depth
      // has no extent, a missing one none at all. The error is already in the report,
      // so skip quietly rather than produce wrong geometry.
      if (part.HasError)
        return null;

      if (part.Generator == PartGenerator.Pipe)
        return BuildPipe(part, report);

      if (!part.Height.HasValue || !part.Width.HasValue)
        return null;

      bool needsDepth = !MakesCurve(part, family);
      bool through = needsDepth && part.DepthThrough;
      if (needsDepth && !part.Depth.HasValue && !through)
        return null;

      double width = ToModel(part.Width.Value);
      double height = ToModel(part.Height.Value);
      double depth = part.Depth.HasValue ? ToModel(part.Depth.Value) : 0;
      double a = ToModel(part.PositionA);
      double b = ToModel(part.PositionB);
      double offset = ToModel(part.Offset);

      if (part.Shape == PartShape.Circle && Math.Abs(width - height) > _tolerance)
        report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): circle uses width as the radius; height differs and is ignored.");
      if (part.Shape == PartShape.Square && Math.Abs(width - height) > _tolerance)
        report.Warnings.Add($"Part '{part.DisplayName}' (line {part.Line}): square uses width as its side; height differs and is ignored.");

      var plane = PartGeometry.ProfilePlane(part.Orientation, a, b, offset);
      PartGeometry.Frame(part.Orientation, out _, out _, out var depthDirection);

      // A negative depth extrudes the other way from the same profile plane. Turning the
      // direction instead of the sign keeps every later step, fillet loops included,
      // working with a positive depth: loop 0 stays on the profile plane.
      if (depth < 0)
      {
        depthDirection = -depthDirection;
        depth = -depth;
      }

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
        Through = through,
      };

      if (needsDepth && !through)
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
    /// A second try at a cutter for a boolean that failed. A plain extrusion is extended at
    /// each end that does not lie inside the solid being cut, so an end flush with a face
    /// moves outward while the floor of a blind pocket stays where it is. A pipe around a
    /// closed curve is rebuilt with its seam turned away from where it was. Null for any
    /// other cutter, and for an extrusion with both ends inside, which are retried as they are.
    /// </summary>
    Brep RetryCutter(Built cutter, Brep solid)
    {
      if (cutter.Rail != null && cutter.Rail.IsClosed)
        return PipeBuilder.Build(cutter.Rail, cutter.Part.RadiusStations, ToModel, _tolerance,
          _doc.ModelAngleToleranceRadians, out _, out _, RetrySeamTurn);

      if (!cutter.CanExtend || cutter.Profile == null)
        return null;

      // The depth axis is a world axis, so the solid's extent along it is one interval.
      var direction = cutter.DepthDirection;
      var axis = new Vector3d(Math.Abs(direction.X), Math.Abs(direction.Y), Math.Abs(direction.Z));
      var box = solid.GetBoundingBox(true);
      double low = new Vector3d(box.Min) * axis, high = new Vector3d(box.Max) * axis;
      double start = new Vector3d(cutter.Plane.Origin) * axis;
      double end = start + (direction * axis) * cutter.Depth;
      bool Inside(double t) => t > low + _tolerance && t < high - _tolerance;

      double margin = Math.Max(_tolerance * 10, cutter.Depth * 0.001);
      double before = Inside(start) ? 0 : margin;
      double after = Inside(end) ? 0 : margin;
      if (before == 0 && after == 0)
        return null;

      var profile = cutter.Profile.DuplicateCurve();
      profile.Translate(-direction * before);
      return PartGeometry.Solid(profile, direction * (cutter.Depth + before + after), _tolerance);
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
        SourceId = template.SourceId,
        SourceAttributes = template.SourceAttributes,
        Changed = template.Changed,
      };

    // ---- Document -----------------------------------------------------------

    void AddSolid(Built built, Brep brep, RunReport report, Dictionary<string, List<Guid>> groups, bool isBase)
    {
      if (built.SourceId != Guid.Empty)
      {
        AddSelected(built, brep, report, isBase);
        return;
      }

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

    /// <summary>
    /// A solid that came from a selected object goes back into that object: the first piece
    /// replaces it, keeping its id and attributes, and any further piece of a cut that split
    /// it is added beside it with a copy of those attributes.
    /// </summary>
    void AddSelected(Built built, Brep brep, RunReport report, bool isBase)
    {
      if (_replaced.Add(built.SourceId))
      {
        if (!_doc.Objects.Replace(built.SourceId, brep))
        {
          report.Errors.Add($"Part '{built.Part.DisplayName}' (line {built.Part.Line}): Rhino refused to replace the selected object {built.SourceId}.");
          return;
        }
        report.ModifiedIds.Add(built.SourceId);
        if (isBase)
          report.BaseIds.Add(built.SourceId);
        return;
      }

      var id = _doc.Objects.AddBrep(brep, built.SourceAttributes.Duplicate());
      if (id == Guid.Empty)
      {
        report.Errors.Add($"Part '{built.Part.DisplayName}' (line {built.Part.Line}): Rhino refused the solid.");
        return;
      }
      report.CreatedIds.Add(id);
      if (isBase)
        report.BaseIds.Add(id);
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
      var segments = LayerSegments(part.LayerName);
      if (segments.Length == 0)
        return _doc.Layers.CurrentLayerIndex;

      int existing = FindLayer(part.LayerName);
      if (existing >= 0)
        return existing;

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

    /// <summary>
    /// The layer a name or a "Parent::Child" path names, or -1 when the model has none. A
    /// plain name matches a layer of that name wherever it sits, as it always has.
    /// </summary>
    int FindLayer(string layerName)
    {
      var segments = LayerSegments(layerName);
      if (segments.Length == 0)
        return -1;
      if (segments.Length == 1)
        return _doc.Layers.FindName(segments[0])?.Index ?? -1;
      return _doc.Layers.FindByFullPath(string.Join(ModelComponent.NamePathSeparator, segments), -1);
    }

    static string[] LayerSegments(string layerName)
      => string.IsNullOrWhiteSpace(layerName)
        ? new string[0]
        : layerName.Split(new[] { ModelComponent.NamePathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();

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
