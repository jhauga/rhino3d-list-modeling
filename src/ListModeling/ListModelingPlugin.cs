using System;
using System.IO;
using Rhino;
using Rhino.Collections;
using Rhino.FileIO;
using Rhino.PlugIns;
using Rhino.UI;

namespace ListModeling
{
  /// <summary>
  /// Registers the List Modeling panel, exposes the scripting object other plugins and
  /// scripts call, and persists each document's embedded list inside the .3dm.
  /// </summary>
  public class ListModelingPlugin : PlugIn
  {
    const int ArchiveVersion = 1;

    public ListModelingPlugin() { Instance = this; }

    public static ListModelingPlugin Instance { get; private set; }

    // Load at startup so the panel tab is available from the Panels menu and
    // sibling plugins can find this one before any command has run.
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
      try
      {
        Panels.RegisterPanel(this, typeof(Views.ListModelingPanel), "List Modeling", LoadPanelIcon());
        RhinoDoc.CloseDocument += OnCloseDocument;
        return LoadReturnCode.Success;
      }
      catch (Exception ex)
      {
        errorMessage = ex.Message;
        return LoadReturnCode.ErrorShowDialog;
      }
    }

    /// <summary>
    /// The object scripts get from rs.GetPlugInObject("ListModeling") and sibling plugins
    /// get from PlugIn.GetPlugInObject. See docs/compatibility.md.
    /// </summary>
    public override object GetPlugInObject() => new ListModelingScriptObject();

    static System.Drawing.Icon LoadPanelIcon()
    {
      var assembly = typeof(ListModelingPlugin).Assembly;
      using (Stream stream = assembly.GetManifestResourceStream("ListModeling.Resources.ListModeling.ico"))
      {
        return stream == null ? null : new System.Drawing.Icon(stream);
      }
    }

    static void OnCloseDocument(object sender, DocumentEventArgs e)
      => Core.ListStore.Clear(e.DocumentSerialNumber);

    // ---- Document persistence -------------------------------------------------

    protected override bool ShouldCallWriteDocument(FileWriteOptions options)
      => !options.WriteGeometryOnly && !options.WriteSelectedObjectsOnly && Core.ListStore.HasAnyText();

    protected override void WriteDocument(RhinoDoc doc, BinaryArchiveWriter archive, FileWriteOptions options)
    {
      var dictionary = new ArchivableDictionary(ArchiveVersion, "ListModeling");
      dictionary.Set("Text", Core.ListStore.GetText(doc.RuntimeSerialNumber));
      archive.WriteDictionary(dictionary);
    }

    protected override void ReadDocument(RhinoDoc doc, BinaryArchiveReader archive, FileReadOptions options)
    {
      var dictionary = archive.ReadDictionary();
      if (dictionary == null)
        return;

      // Importing a file into an open model must not overwrite that model's list.
      if (options.ImportMode || options.ImportReferenceMode)
        return;

      if (dictionary.TryGetString("Text", out var text))
        Core.ListStore.ReplaceText(doc.RuntimeSerialNumber, text);
    }
  }
}
