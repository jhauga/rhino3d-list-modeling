using System;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using ListModeling.Core;
using ListModeling.Core.Runtime;
using Rhino;
using Rhino.UI;

namespace ListModeling.Views
{
  /// <summary>
  /// Dockable panel with the embedded list editor.
  ///
  ///   List Modeling                                  [menu]
  ///   [Load List]
  ///   Embedded List
  ///   (text area)
  ///   [Submit List]
  ///
  /// The menu offers Export List to File, Import File to Embedded List, and Close Panel.
  /// Load List runs a file straight away. Submit List writes the text area to a temp
  /// file, runs that file through the same loader, and removes the temp file afterward,
  /// so both routes exercise one code path.
  ///
  /// Every control carries a stable <see cref="Control.ID"/> (see the Ids class) so the
  /// panel can be driven by id from a script, the way a web page is driven by element id.
  /// The file dialogs are kept in thin wrappers around <see cref="RunListFile"/>,
  /// <see cref="ExportListTo"/> and <see cref="ImportListFrom"/>, which take a path
  /// directly and are what automation calls.
  /// </summary>
  [System.Runtime.InteropServices.Guid("09931521-f9b8-4e81-a52c-c74e309becd0")]
  public class ListModelingPanel : Panel, IPanel
  {
    /// <summary>Control ids, stable across releases so scripts can find controls by name.</summary>
    public static class Ids
    {
      public const string Title = "title";
      public const string Menu = "menu";
      public const string LoadList = "load-list";
      public const string EmbeddedListLabel = "embedded-list-label";
      public const string EmbeddedList = "embedded-list";
      public const string SubmitList = "submit-list";
      public const string MenuExport = "menu-export";
      public const string MenuImport = "menu-import";
      public const string MenuClose = "menu-close";
    }

    readonly uint _docSerial;
    readonly TextArea _textArea;
    bool _refreshing;

    public static Guid PanelId => typeof(ListModelingPanel).GUID;

    public ListModelingPanel(uint documentSerialNumber)
    {
      _docSerial = documentSerialNumber;

      var title = new Label
      {
        ID = Ids.Title,
        Text = "List Modeling",
        Font = SystemFonts.Bold(12),
        VerticalAlignment = VerticalAlignment.Center,
      };

      var menuButton = new Button { ID = Ids.Menu, Text = "≡", ToolTip = "Menu", MinimumSize = new Size(30, 0) };
      menuButton.Click += (s, e) => BuildMenu().Show(menuButton);

      var loadButton = new Button { ID = Ids.LoadList, Text = "Load List", ToolTip = "Pick a .txt or .md list file and run it" };
      loadButton.Click += (s, e) => LoadListFile();

      var embeddedLabel = new Label { ID = Ids.EmbeddedListLabel, Text = "Embedded List", Font = SystemFonts.Bold(11) };

      _textArea = new TextArea
      {
        ID = Ids.EmbeddedList,
        Wrap = false,
        AcceptsTab = true,
        AcceptsReturn = true,
        Font = Eto.Drawing.Fonts.Monospace(9),
        ToolTip = "Embedded List Input for script",
      };
      _textArea.TextChanged += OnTextChanged;

      var submitButton = new Button { ID = Ids.SubmitList, Text = "Submit List", ToolTip = "Run the embedded list" };
      submitButton.Click += (s, e) => SubmitList();

      var layout = new DynamicLayout { DefaultSpacing = new Size(6, 6), Padding = new Padding(8) };
      layout.AddSeparateRow(title, null, menuButton);
      layout.AddSeparateRow(loadButton, null);
      layout.AddSeparateRow(embeddedLabel, null);
      layout.Add(_textArea, xscale: true, yscale: true);
      layout.AddSeparateRow(submitButton, null);
      Content = layout;

      ListStore.TextReplaced += OnStoreTextReplaced;
      RefreshFromStore();
    }

    RhinoDoc Document => RhinoDoc.FromRuntimeSerialNumber(_docSerial) ?? RhinoDoc.ActiveDoc;

    /// <summary>The open panel for a document, or null when the panel is not open.</summary>
    public static ListModelingPanel ForDocument(RhinoDoc doc)
      => doc == null ? null : Panels.GetPanel<ListModelingPanel>(doc);

    /// <summary>The text currently in the editor. Setting it also updates the store.</summary>
    public string EditorText
    {
      get { return _textArea.Text ?? string.Empty; }
      set { _textArea.Text = value ?? string.Empty; }
    }

    // ---- Menu -------------------------------------------------------------------

    /// <summary>Builds the hamburger menu. Separate from showing it so its items can be inspected.</summary>
    public ContextMenu BuildMenu()
    {
      var menu = new ContextMenu();

      var export = new ButtonMenuItem { ID = Ids.MenuExport, Text = "Export List to File" };
      export.Click += (s, e) => ExportListToFile();
      menu.Items.Add(export);

      var import = new ButtonMenuItem { ID = Ids.MenuImport, Text = "Import File to Embedded List" };
      import.Click += (s, e) => ImportFileToEmbeddedList();
      menu.Items.Add(import);

      menu.Items.AddSeparator();

      var close = new ButtonMenuItem { ID = Ids.MenuClose, Text = "Close Panel" };
      close.Click += (s, e) => Panels.ClosePanel(PanelId);
      menu.Items.Add(close);

      return menu;
    }

    void ExportListToFile()
    {
      var dialog = new Eto.Forms.SaveFileDialog { Title = "Export List to File" };
      dialog.Filters.Add(new FileFilter("Markdown list", ".md"));
      dialog.Filters.Add(new FileFilter("Text list", ".txt"));
      if (dialog.ShowDialog(this) != DialogResult.Ok)
        return;

      var path = dialog.FileName;
      if (!Path.HasExtension(path))
        path += dialog.CurrentFilterIndex == 1 ? ".txt" : ".md";

      ExportListTo(path);
    }

    /// <summary>Writes the editor text to a file. Returns false and reports on failure.</summary>
    public bool ExportListTo(string path)
    {
      try
      {
        File.WriteAllText(path, EditorText);
        RhinoApp.WriteLine("List Modeling: exported list to {0}", path);
        return true;
      }
      catch (Exception ex)
      {
        Report("Could not export the list: " + ex.Message);
        return false;
      }
    }

    void ImportFileToEmbeddedList()
    {
      var path = PickListFile("Import File to Embedded List");
      if (path != null)
        ImportListFrom(path);
    }

    /// <summary>Replaces the embedded list with a file's contents without running it.</summary>
    public bool ImportListFrom(string path)
    {
      try
      {
        ListStore.ReplaceText(_docSerial, File.ReadAllText(path));
        RhinoApp.WriteLine("List Modeling: imported {0} into the embedded list.", Path.GetFileName(path));
        return true;
      }
      catch (Exception ex)
      {
        Report("Could not import the file: " + ex.Message);
        return false;
      }
    }

    // ---- Load and submit --------------------------------------------------------

    void LoadListFile()
    {
      var path = PickListFile("Load List");
      if (path != null)
        RunListFile(path);
    }

    /// <summary>Runs a list file against this panel's document and reports the result.</summary>
    public RunReport RunListFile(string path)
    {
      var report = ListRunner.RunFile(Document, path);
      Notifier.Complete(report, showPopup: true);
      return report;
    }

    /// <summary>
    /// Runs the embedded list. The text goes to a temporary file that is run through the
    /// same loader as Load List and removed once the run finishes, so both routes take
    /// one code path. Returns null when the editor is empty.
    /// </summary>
    public RunReport SubmitList()
    {
      var text = EditorText;
      if (string.IsNullOrWhiteSpace(text))
      {
        Report("The embedded list is empty.");
        return null;
      }

      string tempDir = Path.Combine(Path.GetTempPath(), "ListModeling");
      string tempFile = Path.Combine(tempDir, "embedded-" + DateTime.Now.Ticks + ".md");
      try
      {
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(tempFile, text);
        return RunListFile(tempFile);
      }
      catch (Exception ex)
      {
        Report("Could not run the embedded list: " + ex.Message);
        return null;
      }
      finally
      {
        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* best effort */ }
      }
    }

    string PickListFile(string title)
    {
      var dialog = new Eto.Forms.OpenFileDialog { Title = title, MultiSelect = false };
      dialog.Filters.Add(new FileFilter("List files", ".txt", ".md"));
      dialog.Filters.Add(new FileFilter("All files", ".*"));
      return dialog.ShowDialog(this) == DialogResult.Ok ? dialog.FileName : null;
    }

    /// <summary>
    /// Panel-level message. Uses the Rhino message box rather than the Eto one, which has
    /// no top-level window inside a docked panel, and falls back to the command line when
    /// popups are suppressed so an automated run never blocks.
    /// </summary>
    static void Report(string message)
    {
      if (Notifier.SuppressPopups || RhinoApp.IsRunningAutomated || RhinoApp.IsRunningHeadless)
        RhinoApp.WriteLine("List Modeling: " + message);
      else
        Dialogs.ShowMessage(message, "List Modeling");
    }

    // ---- Store sync -------------------------------------------------------------

    void OnTextChanged(object sender, EventArgs e)
    {
      if (!_refreshing)
        ListStore.SetText(_docSerial, _textArea.Text);
    }

    void OnStoreTextReplaced(uint documentSerialNumber)
    {
      if (documentSerialNumber == _docSerial)
        RefreshFromStore();
    }

    void RefreshFromStore()
    {
      var text = ListStore.GetText(_docSerial);
      if (string.Equals(text, _textArea.Text, StringComparison.Ordinal))
        return;
      _refreshing = true;
      try { _textArea.Text = text; }
      finally { _refreshing = false; }
    }

    // ---- IPanel -----------------------------------------------------------------

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
      // Rhino reuses a panel instance when it is reopened, and PanelClosing has
      // already dropped this handler by then, so resubscribe here rather than only
      // in the constructor. Removing first keeps a reopen from subscribing twice.
      ListStore.TextReplaced -= OnStoreTextReplaced;
      ListStore.TextReplaced += OnStoreTextReplaced;
      RefreshFromStore();
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
      => ListStore.TextReplaced -= OnStoreTextReplaced;
  }
}
