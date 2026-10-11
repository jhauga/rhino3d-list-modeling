# Usage

Three ways in: a command with three routes, a dockable panel, and a scripting
object for Python and other plugins.

## The `ListModeling` command

Type `ListModeling` and pick a route:

```text
Select Route ( Import  Steps  GenerateFromSelection )
```

Type `I`, `S` or `G`, or click the option.

### Import

Opens a file picker for a `.txt` or `.md` list, parses it, builds the
geometry, and shows a completion popup with the object count and any warnings.

In scripted mode the path is read from the command line instead, which is how
macros and other plugins call it:

```text
-ListModeling Import "C:\Projects\sample\lists\bracket.md"
```

### Steps

Builds a list one prompt at a time and runs it at the end. The prompts follow
the property order of the syntax:

```text
Element type <Boolean> ( Boolean  BooleanDifference  BooleanUnion  Loft  Revolve  Sweep1  Sweep2 )
Part name <Part 1>
LayerName (Enter for current layer) <Default>
LayerColor (name, #hex, or r,g,b; Enter for default)
Orientation <Top> ( Top  Bottom  Front  Back  Right  Left )
Shape <Rectangle> ( Rectangle  Circle  Ellipse  Polygon )
Height (inches) <1>
Width (inches) <1>
Action <Extrude> ( Extrude  Subtract  Union  Curve )
Depth (inches) <1>
Position (x,y) <0,0>
Add another part <No> ( Yes  No )
```

Numbers are in the model's units. Press `Enter` to accept a default and `Esc`
to cancel; nothing is built until the last prompt is answered.

When the element type is `BooleanDifference` and no part was given the
`subtract` action, one more prompt decides where the cutters come from:

```text
As BooleanDifference <Input> ( Command  Input )
```

- **Command**: the extruded solids are built and selected, then Rhino's own
  `BooleanDifference` takes over with its usual prompt,
  `Select surfaces or polysurfaces to subtract with`. Pick the cutters in the
  viewport and finish the command as normal.
- **Input**: a group identifier is asked for, then the part prompts repeat for
  each cutter with the action fixed to `subtract`. Everything is built in one
  go, and the cutters are applied together as one group.
- **Esc** at that prompt cancels without building.

The list built by Steps is appended to the document's embedded list, so it can
be reviewed, edited and rerun from the panel.

### GenerateFromSelection

Select solids, surfaces, meshes or curves and the plugin writes a list that
describes them. The model decides the values:

- The thinnest bounding box extent is the depth axis, which picks the
  orientation: Z thin gives `top`, Y thin gives `front`, X thin gives `right`.
- The other two extents are the width and height; the box center on that face
  is the position; the box start along the depth axis is written as `offset`
  when it is not zero.
- A planar cap perpendicular to the depth axis is tested for a circle, an
  ellipse or a regular polygon. Anything else is written as a rectangle.
- Objects on the same layer share one `# Element: Boolean` block, and the
  layer name and color are written on every part. A nested layer is written as
  its full path, in the quoted form `"Shop", "Posts"`.
- Curves become `curve` parts, everything else becomes `extrude` parts.

The generated text uses `-` bullets. It is printed to the command line,
appended to the embedded list, and the panel opens to show it. Values are
rounded to four decimals in model units.

Generation describes each object's outer extent only. Holes and other
subtracted features are not inferred, so a board with mount holes comes back
as a plain board; add the `subtract` parts by hand. Fillets and pipes are not
recognized either: a filleted solid comes back as its plain outline, and a
pipe as the box around it.

## The panel

Run `ListModelingPanel` to toggle the panel, or open **List Modeling** from
the Panels menu. It docks and floats like any Rhino panel.

```text
List Modeling                                            [ ≡ ]
[ Load List ]
Embedded List
+------------------------------------------------------------+
|                                                            |
|   (list text)                                              |
|                                                            |
+------------------------------------------------------------+
[ Submit List ]
```

- **Load List** picks a `.txt` or `.md` file and runs it immediately.
- **Embedded List** is a plain text editor. Its content is stored with the
  document and saved inside the `.3dm`, so the list travels with the model.
- **Submit List** writes the editor text to a temporary file, runs that file
  through the same loader as Load List, and deletes the temporary file when
  the run finishes.
- The **≡** menu offers **Export List to File** (save the editor text as
  `.md` or `.txt`), **Import File to Embedded List** (replace the editor text
  with a file's content without running it), and **Close Panel**.

Every run ends with the popup `Script called from 'List Modeling' is
complete`, followed by the object count and any warnings or errors.

## Scripting

The plugin exposes an object through Rhino's standard plugin-object
mechanism. From Python inside Rhino:

```python
import rhinoscriptsyntax as rs

lm = rs.GetPlugInObject("ListModeling")
ok = lm.RunFile(r"C:\Projects\sample\lists\bracket.md")
if not ok:
    print(lm.LastSummary)

text = lm.GenerateFromSelection()   # "" when nothing is selected
lm.SetEmbeddedList(text)            # shows up in the panel
```

| Member | Purpose |
| --- | --- |
| `RunFile(path)` | Parse and run a list file. Returns `True` when no errors occurred. |
| `RunText(text)` | Parse and run list text directly. |
| `GenerateFromSelection()` | List text describing the current selection. |
| `GetEmbeddedList()` / `SetEmbeddedList(text)` | Read or replace the document's embedded list. |
| `LastSummary` | Details of the most recent run. |
| `SuppressPopups` | Set to `True` to stop the panel's completion popup while running in the background, then set it back. |
| `Version` | Plugin version string. |

Script calls report to the command line and never show a popup. How another
plugin reaches the same object is described in
[docs/compatibility.md](compatibility.md).

## Undo and layers

Each run is one undo step named after the list source. Layers named in the
list are created on demand with the given color, and a nested path creates
every level it needs, coloring only the last. Existing layers are reused and
keep their color. Objects carry the part name as their object name, and
grouped parts that survive the run are placed in a Rhino group named after
the group identifier.

## Known limitations

- Surface element types (`Loft`, `Revolve`, `Sweep1`, `Sweep2`, `Patch`,
  `Cap`) produce profile curves only.
- Chamfer and blend are not part of the syntax yet; fillets are, through
  `filletEdge`.
- Profiles are always centered on the position; there is no corner anchoring.
- A boolean that fails even after the retry leaves the solid and the cutters
  in the document for manual work, with a warning.
- The Steps route reads dimensions in model units only; unit suffixes and
  arithmetic are a file feature.
- The Steps route does not prompt for fillets or pipes; write those in a list
  and run it from a file or the panel.
