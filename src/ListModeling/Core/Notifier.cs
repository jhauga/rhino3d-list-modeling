using ListModeling.Core.Runtime;
using Rhino;
using Rhino.UI;

namespace ListModeling.Core
{
  /// <summary>Reports a finished run: always to the command line, and as a popup when a person is watching.</summary>
  public static class Notifier
  {
    public const string CompleteMessage = "Script called from 'List Modeling' is complete";

    /// <summary>
    /// Suppresses the completion popup while set. Background callers that must never
    /// block on a modal dialog set this around their run and clear it afterward. The
    /// sibling plugin contract in docs/compatibility.md relies on it, and the test
    /// suite sets it for the whole session. Command-line reporting is unaffected.
    /// </summary>
    public static bool SuppressPopups { get; set; }

    /// <summary>
    /// True when a modal dialog would have nobody to close it: no popup was asked for,
    /// popups are suppressed, or Rhino is driving itself with no interactive user.
    /// </summary>
    static bool CanShowPopup(bool showPopup)
      => showPopup && !SuppressPopups && !RhinoApp.IsRunningAutomated && !RhinoApp.IsRunningHeadless;

    public static void Complete(RunReport report, bool showPopup)
    {
      foreach (var message in report.Messages)
        RhinoApp.WriteLine("List Modeling: " + message);
      foreach (var warning in report.Warnings)
        RhinoApp.WriteLine("List Modeling warning: " + warning);
      foreach (var error in report.Errors)
        RhinoApp.WriteLine("List Modeling error: " + error);
      RhinoApp.WriteLine(report.StatusLine());

      if (!CanShowPopup(showPopup))
        return;

      var text = CompleteMessage + "." + System.Environment.NewLine + System.Environment.NewLine + report.Summary();
      Dialogs.ShowMessage(text, "List Modeling");
    }
  }
}
