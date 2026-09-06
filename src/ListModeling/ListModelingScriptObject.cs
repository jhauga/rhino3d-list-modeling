using System.Linq;
using System.Runtime.InteropServices;
using ListModeling.Core;
using ListModeling.Core.Generate;
using ListModeling.Core.Runtime;
using Rhino;

namespace ListModeling
{
  /// <summary>
  /// The scripting surface returned by PlugIn.GetPlugInObject. Reachable from Python as
  /// rs.GetPlugInObject("ListModeling") and from other plugins through
  /// Rhino.PlugIns.PlugIn.Find(id).GetPlugInObject(). Every method works on the active
  /// document, reports to the command line, and never shows a dialog.
  /// </summary>
  [ComVisible(true)]
  public class ListModelingScriptObject
  {
    /// <summary>Plugin version, for callers that want to check compatibility.</summary>
    public string Version => typeof(ListModelingScriptObject).Assembly
      .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
      .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
      .Select(a => a.InformationalVersion)
      .FirstOrDefault() ?? "0.0.0";

    /// <summary>Summary of the most recent run, for callers that want details after a false return.</summary>
    public string LastSummary { get; private set; } = string.Empty;

    /// <summary>
    /// Suppresses the completion popup, including the one the panel's own buttons show.
    /// A caller running List Modeling in the background sets this, does its work, and
    /// clears it again. The methods on this object never show a popup either way.
    /// </summary>
    public bool SuppressPopups
    {
      get { return Notifier.SuppressPopups; }
      set { Notifier.SuppressPopups = value; }
    }

    /// <summary>Parses and runs a list file (.txt or .md). Returns true when no errors occurred.</summary>
    public bool RunFile(string path)
    {
      var doc = RhinoDoc.ActiveDoc;
      if (doc == null)
        return false;
      return Finish(ListRunner.RunFile(doc, path));
    }

    /// <summary>Parses and runs list text directly. Returns true when no errors occurred.</summary>
    public bool RunText(string text)
    {
      var doc = RhinoDoc.ActiveDoc;
      if (doc == null)
        return false;
      return Finish(ListRunner.RunText(doc, text, "script text"));
    }

    /// <summary>Describes the current selection as list text; empty when nothing is selected.</summary>
    public string GenerateFromSelection()
    {
      var doc = RhinoDoc.ActiveDoc;
      if (doc == null)
        return string.Empty;
      var selected = doc.Objects.GetSelectedObjects(false, false).ToList();
      return selected.Count == 0 ? string.Empty : SelectionListGenerator.GenerateText(doc, selected);
    }

    /// <summary>The embedded list text stored with the active document.</summary>
    public string GetEmbeddedList()
    {
      var doc = RhinoDoc.ActiveDoc;
      return doc == null ? string.Empty : ListStore.GetText(doc.RuntimeSerialNumber);
    }

    /// <summary>Replaces the embedded list text of the active document; an open panel refreshes.</summary>
    public void SetEmbeddedList(string text)
    {
      var doc = RhinoDoc.ActiveDoc;
      if (doc != null)
        ListStore.ReplaceText(doc.RuntimeSerialNumber, text);
    }

    bool Finish(RunReport report)
    {
      LastSummary = report.Summary();
      Notifier.Complete(report, showPopup: false);
      return report.Succeeded;
    }
  }
}
