using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ListModeling.Core;
using ListModeling.Core.Generate;
using ListModeling.Core.Runtime;
using ListModeling.Core.Syntax;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using Rhino.Input.Custom;
using Rhino.UI;

namespace ListModeling.Commands
{
  /// <summary>
  /// The ListModeling command. First prompt: "Select Route" with three options.
  ///
  ///   Import                 pick a .txt or .md list file and run it
  ///   Steps                  build a list one prompt at a time, then run it
  ///   GenerateFromSelection  describe selected objects as a list in the panel
  ///
  /// In scripted mode ("-ListModeling Import path") the file path is read from the
  /// command line instead of a dialog, which is how sibling plugins call this command.
  /// ScriptRunner style is needed for the Steps route's hand-off to Rhino's own
  /// BooleanDifference command.
  /// </summary>
  [CommandStyle(Style.ScriptRunner)]
  public class ListModelingCommand : Command
  {
    public ListModelingCommand() { Instance = this; }

    public static ListModelingCommand Instance { get; private set; }

    public override string EnglishName => "ListModeling";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      bool interactive = mode == RunMode.Interactive;

      var go = new GetOption();
      go.SetCommandPrompt("Select Route");
      go.AcceptNothing(true);
      int importIndex = go.AddOption("Import");
      int stepsIndex = go.AddOption("Steps");
      int generateIndex = go.AddOption("GenerateFromSelection");

      var result = go.Get();
      if (result == GetResult.Cancel)
        return Result.Cancel;
      if (result != GetResult.Option || go.Option() == null)
        return Result.Nothing;

      int chosen = go.Option().Index;
      if (chosen == importIndex)
        return RunImport(doc, interactive);
      if (chosen == stepsIndex)
        return RunSteps(doc, interactive);
      if (chosen == generateIndex)
        return RunGenerate(doc, interactive);
      return Result.Nothing;
    }

    // ---- Import -----------------------------------------------------------------

    static Result RunImport(RhinoDoc doc, bool interactive)
    {
      string path;
      if (interactive)
      {
        var dialog = new Rhino.UI.OpenFileDialog
        {
          Title = "Load List",
          Filter = "List files (*.txt;*.md)|*.txt;*.md|All files (*.*)|*.*",
        };
        if (!dialog.ShowOpenDialog())
          return Result.Cancel;
        path = dialog.FileName;
      }
      else
      {
        path = string.Empty;
        var rc = RhinoGet.GetString("List file path", false, ref path);
        if (rc != Result.Success)
          return rc;
        path = path.Trim().Trim('"');
      }

      var report = ListRunner.RunFile(doc, path);
      Notifier.Complete(report, showPopup: interactive);
      return report.Succeeded ? Result.Success : Result.Failure;
    }

    // ---- Steps ------------------------------------------------------------------

    static readonly string[] ElementTypeOptions =
      { "Boolean", "BooleanDifference", "BooleanUnion", "Loft", "Revolve", "Sweep1", "Sweep2" };

    static readonly string[] OrientationOptions = { "Top", "Bottom", "Front", "Back", "Right", "Left" };
    static readonly string[] ShapeOptions = { "Rectangle", "Circle", "Ellipse", "Polygon" };
    static readonly string[] ActionOptions = { "Extrude", "Subtract", "Union", "Curve" };

    static Result RunSteps(RhinoDoc doc, bool interactive)
    {
      var list = new ListDocument();

      int typeIndex = AskOption("Element type", ElementTypeOptions, 0);
      if (typeIndex < 0)
        return Result.Cancel;
      var element = new Element("Element", ElementTypeOptions[typeIndex], 0);
      list.Elements.Add(element);

      int partNumber = 1;
      for (;;)
      {
        var part = AskPart(doc, $"Part {partNumber}", null);
        if (part == null)
          return element.Parts.Count == 0 ? Result.Cancel : Result.Nothing;
        element.Parts.Add(part);
        partNumber++;

        int more = AskOption("Add another part", new[] { "Yes", "No" }, 1);
        if (more < 0)
          return Result.Cancel;
        if (more == 1)
          break;
      }

      // BooleanDifference with nothing to subtract yet: ask how to get the cutters.
      bool handOffToRhino = false;
      if (element.Family == ElementFamily.Solid &&
          element.Type.IndexOf("Difference", StringComparison.OrdinalIgnoreCase) >= 0 &&
          !element.Parts.Any(p => p.HasAction(PartAction.Subtract)))
      {
        int how = AskOption("As BooleanDifference", new[] { "Command", "Input" }, 1);
        if (how < 0)
          return Result.Cancel;

        if (how == 0)
        {
          handOffToRhino = true;
        }
        else
        {
          string groupId = string.Empty;
          if (!AskString("Group identifier for the cutters", "cutters", out groupId))
            return Result.Cancel;

          for (;;)
          {
            var cutter = AskPart(doc, $"Cutter {partNumber}", PartAction.Subtract);
            if (cutter == null)
              return Result.Cancel;
            cutter.Group = string.IsNullOrWhiteSpace(groupId) ? null : groupId.Trim();
            element.Parts.Add(cutter);
            partNumber++;

            int more = AskOption("Add another cutter", new[] { "Yes", "No" }, 1);
            if (more < 0)
              return Result.Cancel;
            if (more == 1)
              break;
          }
        }
      }

      // Keep what was built in the embedded list so it can be edited and rerun.
      var text = ListWriter.Write(list);
      ListStore.AppendText(doc.RuntimeSerialNumber, text);

      var report = new ListRunner(doc).Run(list, "steps");

      if (handOffToRhino && report.BaseIds.Count > 0)
      {
        doc.Objects.UnselectAll();
        foreach (var id in report.BaseIds)
          doc.Objects.Select(id);
        doc.Views.Redraw();
        // Rhino now prompts "Select surfaces or polysurfaces to subtract with".
        RhinoApp.RunScript("_BooleanDifference", true);
      }

      Notifier.Complete(report, showPopup: interactive);
      return report.Succeeded ? Result.Success : Result.Failure;
    }

    /// <summary>Prompts for one part. Returns null when the user cancels.</summary>
    static Part AskPart(RhinoDoc doc, string defaultName, PartAction? forcedAction)
    {
      if (!AskString("Part name", defaultName, out var name))
        return null;
      var part = new Part(name, 0) { HasProperties = true };

      string currentLayer = doc.Layers.CurrentLayer != null ? doc.Layers.CurrentLayer.Name : string.Empty;
      if (!AskString("LayerName (Enter for current layer)", forcedAction == PartAction.Subtract ? string.Empty : currentLayer, out var layerName))
        return null;
      part.LayerName = string.IsNullOrWhiteSpace(layerName) ? null : layerName.Trim();

      if (part.LayerName != null)
      {
        if (!AskString("LayerColor (name, #hex, or r,g,b; Enter for default)", string.Empty, out var layerColor))
          return null;
        part.LayerColor = string.IsNullOrWhiteSpace(layerColor) ? null : layerColor.Trim();
      }

      int orientation = AskOption("Orientation", OrientationOptions, 0);
      if (orientation < 0)
        return null;
      part.Orientation = (Orientation)orientation;

      int shape = AskOption("Shape", ShapeOptions, 0);
      if (shape < 0)
        return null;
      part.Shape = (PartShape)shape;
      if (part.Shape == PartShape.Polygon)
      {
        if (!AskNumber("Polygon sides", 6, out var sides, 3))
          return null;
        part.PolygonSides = (int)Math.Round(sides);
      }

      string unitLabel = doc.ModelUnitSystem == UnitSystem.None ? string.Empty : $" ({doc.ModelUnitSystem.ToString().ToLowerInvariant()})";
      string heightLabel = part.Shape == PartShape.Circle ? "Radius" : part.Shape == PartShape.Ellipse ? "Height radius" : "Height";
      string widthLabel = part.Shape == PartShape.Ellipse ? "Width radius" : "Width";

      double width, height;
      if (part.Shape == PartShape.Circle || part.Shape == PartShape.Polygon)
      {
        if (!AskNumber(heightLabel + unitLabel, 1, out width))
          return null;
        height = width;
      }
      else
      {
        if (!AskNumber(heightLabel + unitLabel, 1, out height))
          return null;
        if (!AskNumber(widthLabel + unitLabel, 1, out width))
          return null;
      }
      part.Height = new Measure(height, LengthUnit.Model);
      part.Width = new Measure(width, LengthUnit.Model);

      int action;
      if (forcedAction.HasValue)
      {
        action = (int)forcedAction.Value;
      }
      else
      {
        action = AskOption("Action", ActionOptions, 0);
        if (action < 0)
          return null;
      }
      part.Actions.Add((PartAction)action);

      if (part.PrimaryAction != PartAction.Curve)
      {
        if (!AskNumber("Depth" + unitLabel, 1, out var depth))
          return null;
        part.Depth = new Measure(depth, LengthUnit.Model);
      }

      if (!AskString(PositionPrompt(part.Orientation), "0,0", out var position))
        return null;
      if (!TryParsePair(position, out var a, out var b))
      {
        RhinoApp.WriteLine("List Modeling: position must be two numbers; using 0,0.");
        a = 0; b = 0;
      }
      part.PositionA = new Measure(a, LengthUnit.Model);
      part.PositionB = new Measure(b, LengthUnit.Model);

      return part;
    }

    static string PositionPrompt(Orientation orientation)
    {
      switch (orientation)
      {
        case Orientation.Front:
        case Orientation.Back:
          return "Position (x,z)";
        case Orientation.Right:
        case Orientation.Left:
          return "Position (y,z)";
        default:
          return "Position (x,y)";
      }
    }

    static bool TryParsePair(string text, out double a, out double b)
    {
      a = 0; b = 0;
      var parts = (text ?? string.Empty).Trim().Trim('[', ']', '(', ')').Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
      return parts.Length >= 2
        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b);
    }

    // ---- Generate from selection ------------------------------------------------

    static Result RunGenerate(RhinoDoc doc, bool interactive)
    {
      var go = new GetObject();
      go.SetCommandPrompt("Select objects to describe as a list");
      go.GeometryFilter = ObjectType.Brep | ObjectType.Extrusion | ObjectType.Surface
                        | ObjectType.PolysrfFilter | ObjectType.Curve | ObjectType.Mesh;
      go.GroupSelect = true;
      go.SubObjectSelect = false;
      go.GetMultiple(1, 0);
      if (go.CommandResult() != Result.Success)
        return go.CommandResult();

      var objects = new List<RhinoObject>();
      for (int i = 0; i < go.ObjectCount; i++)
      {
        var obj = go.Object(i).Object();
        if (obj != null)
          objects.Add(obj);
      }

      var list = SelectionListGenerator.Generate(doc, objects);
      foreach (var d in list.Diagnostics)
        RhinoApp.WriteLine("List Modeling: " + d);

      if (list.Elements.Count == 0)
        return Result.Nothing;

      var text = ListWriter.Write(list);
      ListStore.AppendText(doc.RuntimeSerialNumber, text);

      foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        RhinoApp.WriteLine(line);
      RhinoApp.WriteLine($"List Modeling: described {list.AllParts.Count()} part(s); added to the embedded list.");

      if (interactive)
        Panels.OpenPanel(Views.ListModelingPanel.PanelId);

      return Result.Success;
    }

    // ---- Prompt helpers ---------------------------------------------------------

    /// <summary>Command-line option pick. Enter takes the default; returns -1 on cancel.</summary>
    static int AskOption(string prompt, string[] options, int defaultIndex)
    {
      var go = new GetOption();
      go.SetCommandPrompt($"{prompt} <{options[defaultIndex]}>");
      go.AcceptNothing(true);
      var indices = options.Select(o => go.AddOption(o)).ToArray();

      var result = go.Get();
      if (result == GetResult.Cancel)
        return -1;
      if (result == GetResult.Nothing || go.Option() == null)
        return defaultIndex;
      return Array.IndexOf(indices, go.Option().Index);
    }

    static bool AskString(string prompt, string defaultValue, out string value)
    {
      value = defaultValue ?? string.Empty;
      var gs = new GetString();
      gs.SetCommandPrompt(prompt);
      if (!string.IsNullOrEmpty(defaultValue))
        gs.SetDefaultString(defaultValue);
      gs.AcceptNothing(true);

      var result = gs.Get();
      if (result == GetResult.Cancel)
        return false;
      if (result == GetResult.String)
        value = gs.StringResult() ?? string.Empty;
      return true;
    }

    static bool AskNumber(string prompt, double defaultValue, out double value, double lowerLimit = 0)
    {
      value = defaultValue;
      var gn = new GetNumber();
      gn.SetCommandPrompt(prompt);
      gn.SetDefaultNumber(defaultValue);
      gn.SetLowerLimit(lowerLimit, true);
      gn.AcceptNothing(true);

      var result = gn.Get();
      if (result == GetResult.Cancel)
        return false;
      if (result == GetResult.Number)
        value = gn.Number();
      return true;
    }
  }
}
