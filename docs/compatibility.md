# Sibling Plugin Compatibility

`rhino3d-list-modeling` and `rhino3d-markdown-notes` are meant to work as a
pair. This document is the contract between them, written from the List
Modeling side so the notes plugin can be updated against it. Nothing in the
notes plugin has been changed yet.

## Identity

The notes plugin detects List Modeling by plugin id, never by file path:

| Item | Value |
| --- | --- |
| Plugin id | `bc495df2-2c09-4174-b7a2-08371188cae7` |
| Plugin name (`rs.GetPlugInObject`) | `ListModeling` |
| yak package name | `list-modeling` |
| Command, scripted form | `-ListModeling Import "<path>"` |

The id is fixed for the life of the plugin. Detection from C#:

```csharp
using Rhino.PlugIns;

static readonly Guid ListModelingId = new Guid("bc495df2-2c09-4174-b7a2-08371188cae7");

static bool IsListModelingInstalled()
  => PlugIn.GetInstalledPlugIns().ContainsKey(ListModelingId);
```

List Modeling loads at Rhino startup, so `PlugIn.Find(ListModelingId)` returns
the instance whenever the package is installed; `PlugIn.LoadPlugIn(ListModelingId)`
is a safe no-op guard before the first call.

## What the notes plugin does

On panel open, it checks whether List Modeling is installed:

- Not installed: nothing changes.
- Installed: the notes panel gains a right-click menu item
  **Export to 'List Modeling'**.

Choosing that item:

1. Writes the current note text to a temporary file (`.md`).
2. Asks List Modeling to run that file.
3. Waits for the run to finish, then deletes the temporary file.
4. Keeps the notes panel open throughout and shows the completion popup at the
   end.

## How the run is requested

Two equivalent routes; the scripting object is preferred because it returns a
result and never prompts.

### Scripting object (preferred)

```csharp
var plugin = PlugIn.Find(ListModelingId);
dynamic lm = plugin?.GetPlugInObject();
if (lm == null) return;

bool ok = lm.RunFile(tempPath);        // parses, builds, reports to the command line
string details = lm.LastSummary;       // object count, warnings, errors
```

`RunFile` is synchronous and runs on the UI thread; when it returns the
geometry is in the document and the temporary file can be deleted. It never
shows a dialog, so the notes plugin decides how to notify: the List Modeling
convention is a popup titled `List Modeling` with the text
`Script called from 'List Modeling' is complete` followed by the summary.

If the export runs while the List Modeling panel is also open, set
`lm.SuppressPopups = true` first and restore it afterward, so the panel's own
completion popup does not appear on top of the notes panel:

```csharp
bool previous = lm.SuppressPopups;
lm.SuppressPopups = true;
try { lm.RunFile(tempPath); }
finally { lm.SuppressPopups = previous; }
```

`RunText(string)` skips the file entirely, but the temporary file route keeps
both plugins on the same loader path and is what the panel's Submit button
does too.

### Command (fallback)

```csharp
RhinoApp.RunScript($"-_ListModeling _Import \"{tempPath}\"", false);
```

Requires `[CommandStyle(Style.ScriptRunner)]` on the calling command and
cannot report back beyond the command line. Use it only where the scripting
object is not reachable.

## What List Modeling guarantees

- Both scripting members and the scripted `Import` route accept `.md` and
  `.txt`. Markdown that is not list syntax is ignored line by line with info
  diagnostics, so a note with prose around the list still runs.
- One undo record per run, named after the file.
- No modal dialog from `RunFile`, `RunText` or scripted `Import`.
- The embedded list can be read and written through `GetEmbeddedList` and
  `SetEmbeddedList`, so the notes plugin may also hand text to the panel
  instead of running it.
- `SuppressPopups` silences the panel's completion popup as well, for a run
  that has to stay in the background.
- A part the parser reports an error for is skipped rather than built wrong, so
  a note containing a half written list produces the parts that are complete
  and reports the rest.

## Open points for the notes side

- Whether to export the whole note or only the tables and list sections.
- Whether the temporary file goes under `%TEMP%\ListModeling\` (where List
  Modeling writes its own) or under the notes plugin's folder.
- A version check via `lm.Version` before relying on newer members.
