# List Modeling for Rhino

A Rhino 8 plugin that models geometry from a Markdown list. Each part in the
list names its layer, color, orientation, height, width, depth, position and
action, and can round its edges or run as a pipe along a curve; elements
combine parts with boolean operations. Write the list in a file or in the
plugin's dockable panel, run it, and the parts appear on their layers. Select
existing objects and the plugin writes the list for you.

Current version: `0.0.0-alpha.1.1.0` (see [CHANGELOG.md](CHANGELOG.md)).

![A board with two mount holes modeled from the example list](docs/assets/banner.gif)

## Build and Install

Build from source with the .NET SDK, then load the resulting `.rhp` into Rhino
by whichever install route fits.

### Build

Requirements: the .NET 8 SDK (or newer) and Rhino 8 (service release 8.20 or
later recommended).

```bash
dotnet build src/ListModeling/ListModeling.csproj -c Release
```

The plugin builds to `src/ListModeling/bin/Release/net8.0/ListModeling.rhp`.

Rebuilding while Rhino is open works: a running Rhino keeps the loaded `.rhp`
locked, so the build moves the locked file aside (as `ListModeling.rhp.locked-*`)
and writes a fresh one next to it. The new build loads on the next Rhino
restart, and the set-aside copies are cleaned up by later builds once their
Rhino has exited.

### Install to Rhino

Pick one:

1. **Drag and drop**: drag `ListModeling.rhp` from the build output onto an
   open Rhino window. Rhino loads and registers it in place.
2. **Manual copy**: copy the contents of the build output folder anywhere you
   like, then drag the `.rhp` onto Rhino once to register it from there.
3. **Package (yak)**: build a package and install it with Rhino's
   `_PackageManager`.

   ```bash
   dotnet build src/ListModeling/ListModeling.csproj -c Release
   mkdir dist
   cp -R src/ListModeling/bin/Release/net8.0/. dist/
   rm -f dist/*.locked-*
   cp manifest.yml dist/
   cp docs/assets/icon.png dist/
   cd dist
   "/c/Program Files/Rhino 8/System/Yak.exe" build
   ```

   Restart Rhino after installing a package; newly installed packages are only
   scanned at startup.

## Usage

Two commands and a panel:

| Entry point | What it does |
| --- | --- |
| `ListModeling` | Prompts `Select Route ( Import  Steps  GenerateFromSelection )` |
| `ListModelingPanel` | Toggles the **List Modeling** panel (also in the Panels menu) |

- **Import** picks a `.txt` or `.md` list file and builds it.
- **Steps** asks for each property on the command line, one part at a time,
  then builds the list. A `BooleanDifference` without cutters asks whether to
  hand off to Rhino's own `BooleanDifference` command or to type the cutters
  as a group.
- **GenerateFromSelection** describes the selected objects as a list and puts
  it in the panel.

The panel holds an **Embedded List** text area that is saved inside the
`.3dm`. **Load List** runs a file, **Submit List** runs the text area (through
a temporary file that is removed afterward), and the menu exports or imports
the text or closes the panel. Every run ends with a popup:
`Script called from 'List Modeling' is complete`, followed by the object
count and any warnings.

See [docs/usage.md](docs/usage.md) for the prompts and panel in detail.

## Syntax

```markdown
# Element: BooleanDifference

## Circuit Board

* LayerName: Circuit_Board
* LayerColor: Dark Blue
* orientation: top
* height: .84in
* width:  1.68in
* depth:  .04in
* position: 0,0
* actions: extrude

## Mount Holes

### group:holes

- shape: circle
- radius: .05in
- depth:  1in
- position: .62, -.35
- actions: subtract

### group:holes

- shape: circle
- radius: .05in
- depth:  1in
- position: -.62, .35
- actions: subtract
```

- `# Element: <Type>` starts an element: `Boolean`, `BooleanDifference`,
  `BooleanUnion`, or a planned surface type (`Loft`, `Revolve`, `Sweep1`,
  `Sweep2`).
- `## <Name>` starts a part; `### group:<id>` nests grouped parts that act as
  one selection.
- `orientation` picks the face the profile is drawn on and how height, width,
  depth and position map to the axes (`top`, `bottom`, `front`, `back`,
  `right`, `left`).
- Numbers take an optional unit (`in`, `mm`, `cm`, `m`, `ft`); without one
  they are in the model's units. Any number can be arithmetic, such as
  `1.25in/2`.
- `shape` is `rectangle` by default; `circle` and `polygon:N` take a single
  `radius` (which implies a circle on its own), and `ellipse` uses width and
  height as its two radii.
- `LayerName` is optional everywhere; a part without one is built on the
  current layer. `Shop::Posts`, `"Shop", "Posts"` and a nested bullet all name
  a layer inside another.
- `actions` is how the part is made, `extrude`, `curve` or `pipe`, optionally
  followed by what its solid does, `subtract` or `union`.
- `filletEdge` rounds edge loops of an extruded part, one radius per loop or
  edge by edge.
- `pipe` builds a tube of a given `radius` along the part named by `curve`;
  the radius can change along the curve.

```markdown
## Post

- shape: circle
- radius: 1
- depth: 4
- filletEdge:
  - select: 1
  - radius: .25
- actions: extrude
```

The full reference, including the axis table, the fillet edge order and color
formats, is in [docs/syntax.md](docs/syntax.md). Complete examples are
[examples/circuit-board.md](examples/circuit-board.md) and
[examples/filleted-post.md](examples/filleted-post.md).

A longer walk-through of writing a parts list, with small standalone scripts
that build one in Rhino and in FreeCAD:
[Model From a Parts List](https://gist.github.com/jhauga/615902a81b4200e557ca62bb8e99f7ea).

## Scripting

The plugin object is reachable from Python inside Rhino and from other plugins:

```python
import rhinoscriptsyntax as rs

lm = rs.GetPlugInObject("ListModeling")
lm.RunFile(r"C:\Projects\sample\lists\bracket.md")
print(lm.LastSummary)
```

[docs/compatibility.md](docs/compatibility.md) describes the contract used by
the sibling `rhino3d-markdown-notes` plugin to export a note straight into
List Modeling.

## Project Structure

```text
src/ListModeling/             Plugin source (RhinoCommon, Eto panel)
  Core/Syntax/                Parser, writer, units and colors (no Rhino dependency)
  Core/Runtime/               Geometry builder and list runner
  Core/Generate/              List generation from a selection
  Commands/                   ListModeling and ListModelingPanel commands
  Views/                      Eto panel with the embedded list editor
  ListModelingScriptObject.cs Object returned by GetPlugInObject
tests/                        Unit, smoke, regression and UI suites
docs/                         Syntax, usage, roadmap and sibling compatibility
  assets/                     Images and other files the documentation links to
examples/                     Ready-to-run lists
manifest.yml                  yak package manifest
```

## Tests

```powershell
powershell -File tests\run-tests.ps1                 # all four suites
powershell -File tests\run-tests.ps1 -Suite unit     # fast, no Rhino needed
```

The unit suite runs the syntax layer through xUnit without Rhino. The smoke,
regression and UI suites run inside Rhino: the runner builds the plugin, starts
Rhino with the freshly built binary, and reads the report it writes. The UI
suite drives the panel by control id, click and keystroke, the way a Playwright
test drives a page. See [tests/README.md](tests/README.md) for the layout,
the golden file workflow and the two checks that have to be done by hand.

## Roadmap

The long-term goal is publishing to the Rhino package marketplace as
`list-modeling`. The syntax is built to grow: fillets and pipes are in, and
surface elements (loft, revolve, sweep, patch, cap) and the remaining tooling
(chamfer, blend) come next, as described in [docs/roadmap.md](docs/roadmap.md).
Task-level work is tracked in [TODO.md](TODO.md).

## License

MIT License - Feel free to use and modify as you wish! The full text is in
[LICENSE](LICENSE).
