using System;
using System.Collections.Generic;
using System.Linq;
using ListModeling.Core.Syntax;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ListModeling.Core.Runtime
{
  /// <summary>
  /// Steps: what a sequence does to the run's selection, one step after another.
  ///
  ///   select, deselect, deselectAll   change the selection (see Find for targets)
  ///   offset                          a new curve beside each selected curve
  ///   extrude                         selected closed planar curves become solids
  ///   booleanDifference               selected solids are cut by a target's solids
  ///   copy, paste                     remember the selection, then copy it in place
  ///   changeLayer                     move the selection to a layer
  ///   delete                          remove the selection
  ///
  /// A step that reports an error stops its header; steps already done stay done and the
  /// rest of the list still runs. Each step works out everything it will change before it
  /// changes anything, so a failed step leaves the model as it was.
  /// </summary>
  public sealed partial class ListRunner
  {
    // ---- Ownership ----------------------------------------------------------

    /// <summary>Objects each header owns, by name key, in the order they were made.</summary>
    readonly Dictionary<string, List<Guid>> _owned = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);

    /// <summary>The name keys that own each object.</summary>
    readonly Dictionary<Guid, List<string>> _ownersOf = new Dictionary<Guid, List<string>>();

    /// <summary>Objects this run added, so the report never counts one of them as changed.</summary>
    readonly HashSet<Guid> _createdByRun = new HashSet<Guid>();

    static string GroupKey(string group) => "group:" + ListParser.NameKey(group);

    /// <summary>The object belongs to the part's name, every header above it, and its group.</summary>
    void Own(Part part, Guid id)
    {
      var names = part.Path.Count > 0 ? part.Path : new List<string> { part.Name };
      foreach (var name in names)
        OwnKey(ListParser.NameKey(name), id);
      if (!string.IsNullOrEmpty(part.Group))
        OwnKey(GroupKey(part.Group), id);
    }

    /// <summary>The new object belongs to the same headers as an earlier one.</summary>
    void OwnLike(Guid original, Guid id)
    {
      if (_ownersOf.TryGetValue(original, out var keys))
        foreach (var key in keys.ToList())
          OwnKey(key, id);
    }

    void OwnKey(string key, Guid id)
    {
      if (!_owned.TryGetValue(key, out var ids))
        _owned[key] = ids = new List<Guid>();
      if (!ids.Contains(id))
        ids.Add(id);
      if (!_ownersOf.TryGetValue(id, out var keys))
        _ownersOf[id] = keys = new List<string>();
      if (!keys.Contains(key))
        keys.Add(key);
    }

    /// <summary>A removed object leaves every header, the selection and what copy remembered.</summary>
    void Disown(Guid id)
    {
      if (_ownersOf.TryGetValue(id, out var keys))
      {
        foreach (var key in keys)
          if (_owned.TryGetValue(key, out var ids))
            ids.Remove(id);
        _ownersOf.Remove(id);
      }
      Deselect(id);
      _copied.Remove(id);
    }

    /// <summary>Objects a name key owns that are still in the model, in the order made.</summary>
    List<Guid> Owned(string key)
      => _owned.TryGetValue(key, out var ids) ? ids.Where(Alive).ToList() : new List<Guid>();

    bool Alive(Guid id) => _doc.Objects.FindId(id) != null;

    // ---- Report ---------------------------------------------------------------

    void Created(RunReport report, Guid id, bool isBase)
    {
      _createdByRun.Add(id);
      report.CreatedIds.Add(id);
      if (isBase)
        report.BaseIds.Add(id);
    }

    /// <summary>Counts an object as changed only when it was in the model before the run.</summary>
    void Changed(RunReport report, Guid id)
    {
      if (!_createdByRun.Contains(id) && !report.ModifiedIds.Contains(id))
        report.ModifiedIds.Add(id);
    }

    /// <summary>An object the run made and then removed is no longer counted as added.</summary>
    void Removed(RunReport report, Guid id)
    {
      if (_createdByRun.Contains(id))
      {
        report.CreatedIds.Remove(id);
        report.BaseIds.Remove(id);
      }
      else
        Changed(report, id);
      Disown(id);
    }

    // ---- Selection ------------------------------------------------------------

    /// <summary>The run's own selection, in the order objects joined it.</summary>
    readonly List<Guid> _selection = new List<Guid>();
    readonly HashSet<Guid> _selectionSet = new HashSet<Guid>();

    /// <summary>What copy remembered, for paste.</summary>
    readonly List<Guid> _copied = new List<Guid>();

    /// <summary>True once a select part, select, deselect or deselectAll ran.</summary>
    bool _usedSelection;

    void Select(Guid id)
    {
      if (_selectionSet.Add(id))
        _selection.Add(id);
    }

    /// <summary>A new piece of a selected object is selected with it.</summary>
    void SelectBeside(Guid source, Guid id)
    {
      if (_selectionSet.Contains(source))
        Select(id);
    }

    void Deselect(Guid id)
    {
      if (_selectionSet.Remove(id))
        _selection.Remove(id);
    }

    void ClearSelection()
    {
      _selection.Clear();
      _selectionSet.Clear();
    }

    void SetSelection(IEnumerable<Guid> ids)
    {
      ClearSelection();
      foreach (var id in ids)
        Select(id);
    }

    List<Guid> SelectedObjects() => _selection.Where(Alive).ToList();

    /// <summary>A list that selected or deselected leaves its selection showing in the viewport.</summary>
    void ShowSelection()
    {
      if (!_usedSelection)
        return;
      _doc.Objects.UnselectAll();
      foreach (var id in SelectedObjects())
        _doc.Objects.Select(id, true);
    }

    // ---- Targets --------------------------------------------------------------

    /// <summary>
    /// The objects a target names, in order, or null with the reason. Looked up in this
    /// order, the first place that has anything winning:
    ///   1. the headers of this run, matched the way header names always are;
    ///   2. objects already in the model whose object name is that text;
    ///   3. a layer of that name or path, read like LayerName.
    /// A group target reads the run's group, then a group of that name in the model; a
    /// target from a LayerName line reads layers only. In the model only objects a person
    /// could select count. An index then picks one object, counting from 0.
    /// </summary>
    List<Guid> Find(StepTarget target, string verb, out string problem)
    {
      problem = null;
      List<Guid> found;
      string what;

      if (target.Group != null)
      {
        what = "group:" + target.Group;
        found = Owned(GroupKey(target.Group));
        if (found.Count == 0)
          found = InModelGroup(target.Group);
        if (found.Count == 0)
        {
          problem = $"nothing is in group '{target.Group}': no part of this list is in it, and the model has no group of that name with objects that can be selected";
          return null;
        }
      }
      else if (target.ByLayer)
      {
        what = target.LayerPath;
        int layer = FindLayer(target.LayerPath);
        if (layer < 0)
        {
          problem = $"no layer '{target.LayerPath}' in the model; nothing to {verb}";
          return null;
        }
        found = OnLayer(layer);
        if (found.Count == 0)
        {
          problem = $"no visible, unlocked object on layer '{target.LayerPath}'; nothing to {verb}";
          return null;
        }
      }
      else
      {
        what = target.Name;
        found = Owned(ListParser.NameKey(target.Name));
        if (found.Count == 0)
          found = NamedInModel(target.Name);
        if (found.Count == 0)
        {
          int layer = FindLayer(target.LayerPath ?? target.Name);
          if (layer >= 0)
            found = OnLayer(layer);
        }
        if (found.Count == 0)
        {
          problem = $"nothing is named '{target.Name}': no header in this list built it, no object in the model has that name, and no layer is called that";
          return null;
        }
      }

      if (target.Index.HasValue)
      {
        int index = target.Index.Value;
        if (index >= found.Count)
        {
          problem = $"'{what}' has {found.Count} object{(found.Count == 1 ? "" : "s")}, counting from 0; [{index}] is not one of them";
          return null;
        }
        found = new List<Guid> { found[index] };
      }
      return found;
    }

    static ObjectEnumeratorSettings Selectable() => new ObjectEnumeratorSettings
    {
      NormalObjects = true,
      LockedObjects = false,
      HiddenObjects = false,
      DeletedObjects = false,
      IncludeLights = false,
      IncludeGrips = false,
    };

    IEnumerable<RhinoObject> SelectableObjects(ObjectEnumeratorSettings settings)
      => _doc.Objects.GetObjectList(settings).Where(obj => obj.IsSelectable(true, true, false, false));

    List<Guid> NamedInModel(string name)
    {
      var key = ListParser.NameKey(name);
      return SelectableObjects(Selectable())
        .Where(obj => !string.IsNullOrWhiteSpace(obj.Attributes.Name) && ListParser.NameKey(obj.Attributes.Name) == key)
        .Select(obj => obj.Id)
        .ToList();
    }

    List<Guid> OnLayer(int layerIndex)
    {
      var settings = Selectable();
      settings.LayerIndexFilter = layerIndex;
      return SelectableObjects(settings).Select(obj => obj.Id).ToList();
    }

    List<Guid> InModelGroup(string name)
    {
      var group = _doc.Groups.FindName(name);
      if (group == null)
        return new List<Guid>();
      return _doc.Objects.FindByGroup(group.Index)
        .Where(obj => obj.IsSelectable(true, true, false, false))
        .Select(obj => obj.Id)
        .ToList();
    }

    // ---- Sequences --------------------------------------------------------------

    /// <summary>Runs a sequence's steps in order until one reports an error.</summary>
    void RunSequence(Part part, RunReport report)
    {
      // A step the parser could not read means none of them run; the error is reported.
      if (part.HasError)
        return;

      for (int i = 0; i < part.Steps.Count; i++)
      {
        var step = part.Steps[i];
        string problem;
        try
        {
          problem = RunStep(part, step, report);
        }
        catch (Exception ex)
        {
          problem = $"'{StepWords.Spelling(step.Kind)}' failed unexpectedly: {ex.Message}";
        }
        if (problem == null)
          continue;

        bool more = i < part.Steps.Count - 1;
        report.Errors.Add($"Part '{part.DisplayName}' (line {step.Line}): {problem}{(more ? "; the rest of its steps are skipped." : ".")}");
        return;
      }
    }

    /// <summary>Runs one step; null when it worked, otherwise why it could not.</summary>
    string RunStep(Part part, Step step, RunReport report)
    {
      switch (step.Kind)
      {
        case StepKind.Select:
        {
          var found = Find(step.Target, "select", out var problem);
          if (found == null)
            return problem;
          foreach (var id in found)
            Select(id);
          _usedSelection = true;
          return null;
        }

        case StepKind.Deselect:
        {
          var found = Find(step.Target, "deselect", out var problem);
          if (found == null)
            return problem;
          foreach (var id in found)
            Deselect(id);
          _usedSelection = true;
          return null;
        }

        case StepKind.DeselectAll:
          ClearSelection();
          _usedSelection = true;
          return null;

        case StepKind.ChangeLayer: return ChangeLayerStep(part, step, report);
        case StepKind.Delete: return DeleteStep(report);
        case StepKind.Copy: return CopyStep();
        case StepKind.Paste: return PasteStep(part, step, report);
        case StepKind.Offset: return OffsetStep(part, step, report);
        case StepKind.Extrude: return ExtrudeStep(part, step, report);
        case StepKind.BooleanDifference: return BooleanDifferenceStep(part, step, report);
        default: return $"'{StepWords.Spelling(step.Kind)}' is not a step this release can run";
      }
    }

    static string NothingSelected(StepKind kind) => $"nothing is selected for '{StepWords.Spelling(kind)}'";

    /// <summary>"the selected object is not a curve, which 'offset' needs", or "none of the 3 selected objects is ...".</summary>
    static string NoneUsable(int count, string one, string step)
      => count == 1
        ? $"the selected object is not {one}, which '{step}' needs"
        : $"none of the {count} selected objects is {one}, which '{step}' needs";

    string ObjectLabel(Guid id)
    {
      var name = _doc.Objects.FindId(id)?.Attributes.Name;
      return string.IsNullOrWhiteSpace(name) ? id.ToString() : name.Trim();
    }

    /// <summary>"offset needs curves; 2 selected objects that are not curves are left alone."</summary>
    void WarnLeftAlone(Part part, Step step, RunReport report, int count, string one, string many)
    {
      if (count == 1)
        report.Warnings.Add($"Part '{part.DisplayName}' (line {step.Line}): {StepWords.Spelling(step.Kind)} needs {many}; 1 selected object that is not {one} is left alone.");
      else if (count > 1)
        report.Warnings.Add($"Part '{part.DisplayName}' (line {step.Line}): {StepWords.Spelling(step.Kind)} needs {many}; {count} selected objects that are not {many} are left alone.");
    }

    // ---- changeLayer, delete, copy, paste ---------------------------------------------

    string ChangeLayerStep(Part part, Step step, RunReport report)
    {
      var ids = SelectedObjects();
      if (ids.Count == 0)
        return NothingSelected(step.Kind);

      int layer = ResolveLayer(step.LayerPath, part.LayerColor, part, report);
      foreach (var id in ids)
      {
        var obj = _doc.Objects.FindId(id);
        if (obj.Attributes.LayerIndex == layer)
          continue;
        var attributes = obj.Attributes.Duplicate();
        attributes.LayerIndex = layer;
        if (!_doc.Objects.ModifyAttributes(obj, attributes, true))
          return $"Rhino refused to move '{ObjectLabel(id)}' to layer '{step.LayerPath}'";
        Changed(report, id);
      }
      return null;
    }

    string DeleteStep(RunReport report)
    {
      var ids = SelectedObjects();
      if (ids.Count == 0)
        return NothingSelected(StepKind.Delete);
      foreach (var id in ids)
      {
        if (!_doc.Objects.Delete(id, true))
          return $"Rhino refused to delete '{ObjectLabel(id)}'";
        Removed(report, id);
      }
      ClearSelection();
      return null;
    }

    string CopyStep()
    {
      var ids = SelectedObjects();
      if (ids.Count == 0)
        return NothingSelected(StepKind.Copy);
      _copied.Clear();
      _copied.AddRange(ids);
      return null;
    }

    /// <summary>A copy of each remembered object in the same place, with its name, layer and attributes.</summary>
    string PasteStep(Part part, Step step, RunReport report)
    {
      var originals = _copied.Where(Alive).ToList();
      if (originals.Count == 0)
        return "nothing is copied for 'paste'; write 'copy' before it";

      var copies = new List<Guid>();
      foreach (var original in originals)
      {
        var obj = _doc.Objects.FindId(original);
        var attributes = obj.Attributes.Duplicate();
        // A copy starts outside the groups of its original, as a pasted object does.
        attributes.RemoveFromAllGroups();
        var id = _doc.Objects.Add(obj.Geometry.Duplicate(), attributes);
        if (id == Guid.Empty)
          return $"Rhino refused the copy of '{ObjectLabel(original)}'";
        Created(report, id, false);
        // A copy is a copy: it belongs to the headers of its original, after it.
        OwnLike(original, id);
        copies.Add(id);
      }
      SetSelection(copies);
      return null;
    }

    // ---- offset -----------------------------------------------------------------------

    /// <summary>The direction a view of the orientation looks from, which decides left and right.</summary>
    static Vector3d ViewNormal(Orientation orientation)
    {
      switch (orientation)
      {
        case Orientation.Bottom: return -Vector3d.ZAxis;
        case Orientation.Front: return -Vector3d.YAxis;
        case Orientation.Back: return Vector3d.YAxis;
        case Orientation.Right: return Vector3d.XAxis;
        case Orientation.Left: return -Vector3d.XAxis;
        default: return Vector3d.ZAxis;
      }
    }

    string OffsetStep(Part part, Step step, RunReport report)
    {
      var ids = SelectedObjects();
      if (ids.Count == 0)
        return NothingSelected(step.Kind);

      var curves = ids.Where(id => _doc.Objects.FindId(id).Geometry is Curve).ToList();
      WarnLeftAlone(part, step, report, ids.Count - curves.Count, "a curve", "curves");
      if (curves.Count == 0)
        return NoneUsable(ids.Count, "a curve", "offset");

      double distance = ToModel(step.Distance.Value);
      var made = new List<KeyValuePair<Guid, Curve[]>>();
      foreach (var id in curves)
      {
        var curve = (Curve)_doc.Objects.FindId(id).Geometry;
        var result = OffsetCurve(curve, distance, part.Orientation, out var problem);
        if (result == null)
          return $"the curve '{ObjectLabel(id)}' could not be offset: {problem}";
        made.Add(new KeyValuePair<Guid, Curve[]>(id, result));
      }

      int layer = string.IsNullOrWhiteSpace(part.LayerName) ? -1 : ResolveLayer(part, report);
      var added = new List<Guid>();
      foreach (var pair in made)
      {
        var source = _doc.Objects.FindId(pair.Key);
        foreach (var curve in pair.Value)
        {
          // A copy of the source's attributes, named after the step's header.
          var attributes = source.Attributes.Duplicate();
          attributes.RemoveFromAllGroups();
          attributes.Name = part.Name;
          if (layer >= 0)
            attributes.LayerIndex = layer;
          var id = _doc.Objects.AddCurve(curve, attributes);
          if (id == Guid.Empty)
            return $"Rhino refused the offset of '{ObjectLabel(pair.Key)}'";
          Created(report, id, false);
          Own(part, id);
          added.Add(id);
        }
      }
      SetSelection(added);
      return null;
    }

    /// <summary>
    /// Offsets a planar curve. A closed curve grows for a positive distance and shrinks for
    /// a negative one. An open curve moves to its left for a positive distance, as seen
    /// from the side the orientation faces (the Top view for top), and to its right for a
    /// negative one. The side is checked on the result rather than trusted to a convention.
    /// Corners stay sharp.
    /// </summary>
    Curve[] OffsetCurve(Curve curve, double distance, Orientation orientation, out string problem)
    {
      problem = null;
      var viewer = ViewNormal(orientation);
      Plane plane;
      if (curve.IsLinear(_tolerance))
      {
        plane = new Plane(curve.PointAtStart, viewer);
        if (!plane.IsValid || Math.Abs(curve.TangentAtStart * viewer) > 1 - 1e-9)
        {
          problem = "a straight line along the orientation's own axis has no side to offset to";
          return null;
        }
      }
      else if (!curve.TryGetPlane(out plane, _tolerance))
      {
        problem = "it is not planar";
        return null;
      }
      if (plane.Normal * viewer < 0)
        plane.Flip();

      Curve[] Make(double d) => curve.Offset(plane, d, _tolerance, CurveOffsetCornerStyle.Sharp);

      var result = Make(distance);
      if (result == null || result.Length == 0)
      {
        problem = "Rhino could not offset it by that distance";
        return null;
      }

      bool wrongSide;
      if (curve.IsClosed)
      {
        double before = Area(new[] { curve });
        double after = Area(result);
        wrongSide = distance > 0 ? after < before : after > before;
      }
      else
      {
        double t = curve.Domain.Mid;
        var point = curve.PointAt(t);
        var left = Vector3d.CrossProduct(plane.Normal, curve.TangentAt(t));
        var nearest = result.Select(c => c.ClosestPoint(point, out var s) ? c.PointAt(s) : point)
          .OrderBy(p => p.DistanceTo(point)).First();
        double side = (nearest - point) * left;
        wrongSide = distance > 0 ? side < 0 : side > 0;
      }

      if (wrongSide)
      {
        result = Make(-distance);
        if (result == null || result.Length == 0)
        {
          problem = "Rhino could not offset it by that distance";
          return null;
        }
      }
      return result;
    }

    static double Area(IEnumerable<Curve> curves)
    {
      double total = 0;
      foreach (var curve in curves)
      {
        var mass = curve.IsClosed ? AreaMassProperties.Compute(curve) : null;
        if (mass != null)
          total += Math.Abs(mass.Area);
      }
      return total;
    }

    // ---- extrude ----------------------------------------------------------------------

    /// <summary>
    /// The selected closed planar curves become solids and are removed. Curves lying in one
    /// plane are read as regions, so a closed curve inside another makes a hole. The
    /// direction is the depth axis of the header's orientation when the curves' plane is
    /// perpendicular to it, otherwise the plane's normal; a negative distance goes the other
    /// way. Each solid takes the attributes and headers of the first curve of its region.
    /// </summary>
    string ExtrudeStep(Part part, Step step, RunReport report)
    {
      var ids = SelectedObjects();
      if (ids.Count == 0)
        return NothingSelected(step.Kind);

      var curveIds = ids.Where(id => _doc.Objects.FindId(id).Geometry is Curve).ToList();
      WarnLeftAlone(part, step, report, ids.Count - curveIds.Count, "a curve", "curves");
      if (curveIds.Count == 0)
        return NoneUsable(ids.Count, "a curve", "extrude");

      var curves = curveIds.Select(id => (Curve)_doc.Objects.FindId(id).Geometry).ToList();
      for (int i = 0; i < curves.Count; i++)
      {
        if (!curves[i].IsClosed)
          return $"'{ObjectLabel(curveIds[i])}' is an open curve; 'extrude' needs closed planar curves (surfaces from open curves are planned)";
        if (!curves[i].IsPlanar(_tolerance))
          return $"'{ObjectLabel(curveIds[i])}' is not planar; 'extrude' needs closed planar curves";
      }

      var regions = Brep.CreatePlanarBreps(curves, _tolerance);
      if (regions == null || regions.Length == 0)
        return "the selected curves give no region to extrude; they may touch or cross";

      PartGeometry.Frame(part.Orientation, out _, out _, out var depthAxis);
      double distance = ToModel(step.Distance.Value);

      // Work out every solid before anything in the model changes.
      var solids = new List<KeyValuePair<int, Brep>>();
      var used = new HashSet<int>();
      foreach (var region in regions)
      {
        var face = region.Faces[0];
        if (!face.TryGetPlane(out var plane, _tolerance))
          return "a region of the selected curves is not planar";
        var normal = plane.Normal;
        normal.Unitize();
        var direction = Math.Abs(normal * depthAxis) > 1 - 1e-6 ? depthAxis : normal;
        if (distance < 0)
          direction = -direction;

        var start = face.PointAt(face.Domain(0).Mid, face.Domain(1).Mid);
        var path = new LineCurve(start, start + direction * Math.Abs(distance));
        var solid = face.CreateExtrusion(path, true);
        if (solid == null || !solid.IsSolid)
          return "Rhino could not extrude a region of the selected curves";
        if (solid.SolidOrientation == BrepSolidOrientation.Inward)
          solid.Flip();

        int first = -1;
        for (int i = 0; i < curves.Count; i++)
        {
          if (!OnBoundary(region, curves[i]))
            continue;
          used.Add(i);
          if (first < 0)
            first = i;
        }
        if (first < 0)
          first = 0;
        solids.Add(new KeyValuePair<int, Brep>(first, solid));
      }

      int layer = string.IsNullOrWhiteSpace(part.LayerName) ? -1 : ResolveLayer(part, report);
      var added = new List<Guid>();
      foreach (var pair in solids)
      {
        var source = _doc.Objects.FindId(curveIds[pair.Key]);
        var attributes = source.Attributes.Duplicate();
        attributes.RemoveFromAllGroups();
        if (layer >= 0)
          attributes.LayerIndex = layer;
        var id = _doc.Objects.AddBrep(pair.Value, attributes);
        if (id == Guid.Empty)
          return "Rhino refused an extruded solid";
        Created(report, id, false);
        OwnLike(curveIds[pair.Key], id);
        added.Add(id);
      }

      // The curves have become the solids.
      foreach (int i in used)
        if (_doc.Objects.Delete(curveIds[i], true))
          Removed(report, curveIds[i]);

      SetSelection(added);
      return null;
    }

    /// <summary>True when a curve lies on an edge of a region, which makes it one of the region's curves.</summary>
    bool OnBoundary(Brep region, Curve curve)
    {
      var point = curve.PointAt(curve.Domain.Mid);
      double reach = 10 * _tolerance;
      foreach (var edge in region.Edges)
        if (edge.ClosestPoint(point, out var t) && edge.PointAt(t).DistanceTo(point) <= reach)
          return true;
      return false;
    }

    // ---- booleanDifference ------------------------------------------------------------

    /// <summary>
    /// Each selected closed solid is cut by the target's closed solids whose bounding boxes
    /// reach it. A cut solid is replaced in place and keeps its id; a result in several
    /// pieces adds the others beside it. The cutters that cut are removed; one that reaches
    /// no selected solid is left with a warning. A cut that fails is tried once more at a
    /// tenth of the tolerance; when it fails again nothing changes.
    /// </summary>
    string BooleanDifferenceStep(Part part, Step step, RunReport report)
    {
      var ids = SelectedObjects();
      if (ids.Count == 0)
        return NothingSelected(step.Kind);

      var found = Find(step.Target, "cut with", out var problem);
      if (found == null)
        return problem;

      var cutters = new List<KeyValuePair<Guid, Brep>>();
      foreach (var id in found)
      {
        var brep = SolidOf(_doc.Objects.FindId(id));
        if (brep != null)
          cutters.Add(new KeyValuePair<Guid, Brep>(id, brep));
      }
      if (cutters.Count == 0)
        return $"'{step.Target.Text}' holds no closed solid to cut with";

      // An object that is both selected and a cutter is a cutter.
      var cutterIds = new HashSet<Guid>(cutters.Select(c => c.Key));
      var candidates = ids.Where(id => !cutterIds.Contains(id)).ToList();
      var solids = new List<KeyValuePair<Guid, Brep>>();
      foreach (var id in candidates)
      {
        var brep = SolidOf(_doc.Objects.FindId(id));
        if (brep != null)
          solids.Add(new KeyValuePair<Guid, Brep>(id, brep));
      }
      WarnLeftAlone(part, step, report, candidates.Count - solids.Count, "a closed solid", "closed solids");
      if (solids.Count == 0)
        return "no closed solid is selected for 'booleanDifference' to cut";

      // Work out every cut before anything in the model changes.
      var results = new List<KeyValuePair<Guid, Brep[]>>();
      var usedCutters = new HashSet<Guid>();
      foreach (var solid in solids)
      {
        var reach = solid.Value.GetBoundingBox(true);
        reach.Inflate(_tolerance);
        var reaching = cutters.Where(c => Overlaps(reach, c.Value.GetBoundingBox(true))).ToList();
        if (reaching.Count == 0)
          continue;

        var result = Brep.CreateBooleanDifference(new[] { solid.Value }, reaching.Select(c => c.Value), _tolerance);
        if (result == null || result.Length == 0)
          result = Brep.CreateBooleanDifference(new[] { solid.Value }, reaching.Select(c => c.Value), _tolerance / 10);
        if (result == null || result.Length == 0)
          return $"cutting '{ObjectLabel(solid.Key)}' with '{step.Target.Text}' failed, also at a tenth of the tolerance; nothing is changed";

        results.Add(new KeyValuePair<Guid, Brep[]>(solid.Key, result));
        foreach (var cutter in reaching)
          usedCutters.Add(cutter.Key);
      }

      foreach (var pair in results)
      {
        if (!_doc.Objects.Replace(pair.Key, pair.Value[0]))
          return $"Rhino refused to replace '{ObjectLabel(pair.Key)}' with its cut shape";
        Changed(report, pair.Key);

        var original = _doc.Objects.FindId(pair.Key);
        for (int i = 1; i < pair.Value.Length; i++)
        {
          var attributes = original.Attributes.Duplicate();
          attributes.RemoveFromAllGroups();
          var id = _doc.Objects.AddBrep(pair.Value[i], attributes);
          if (id == Guid.Empty)
            return $"Rhino refused a piece of '{ObjectLabel(pair.Key)}'";
          Created(report, id, false);
          OwnLike(pair.Key, id);
          Select(id);
        }
      }

      foreach (var cutter in cutters)
      {
        if (usedCutters.Contains(cutter.Key))
        {
          if (_doc.Objects.Delete(cutter.Key, true))
            Removed(report, cutter.Key);
        }
        else
          report.Warnings.Add($"Part '{part.DisplayName}' (line {step.Line}): '{ObjectLabel(cutter.Key)}' from '{step.Target.Text}' reaches none of the selected solids, so it cuts nothing; it is left in the model.");
      }
      return null;
    }
  }
}
