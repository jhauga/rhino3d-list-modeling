using Rhino;
using Rhino.Commands;
using Rhino.UI;

namespace ListModeling.Commands
{
  /// <summary>Toggles the List Modeling panel. No prompts, so it is safe in scripted mode.</summary>
  public class ListModelingPanelCommand : Command
  {
    public ListModelingPanelCommand() { Instance = this; }

    public static ListModelingPanelCommand Instance { get; private set; }

    public override string EnglishName => "ListModelingPanel";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
      var panelId = Views.ListModelingPanel.PanelId;

      if (Panels.IsPanelVisible(panelId))
      {
        Panels.ClosePanel(panelId);
        RhinoApp.WriteLine("List Modeling panel hidden.");
      }
      else
      {
        Panels.OpenPanel(panelId);
      }

      return Result.Success;
    }
  }
}
