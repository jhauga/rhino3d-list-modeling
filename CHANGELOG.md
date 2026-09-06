# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.0.0-alpha.1] - 2026-09-06

### Added

- Initial plugin targeting Rhino 8 (`net8.0`, cross-platform Eto UI).
- Markdown list syntax: `# Element: <Type>` headers, `## <Part>` headers,
  `### group:<id>` grouped parts, and bullet properties for layer name, layer
  color, orientation, shape, height, width, depth, position, offset and
  actions. Numbers accept `in`, `mm`, `cm`, `m` and `ft` suffixes and are
  converted to the document's model units; unitless numbers are model units.
- Orientation axis mapping for `top`, `bottom`, `front`, `back`, `right` and
  `left`, with the profile centered on the position and extruded along the
  depth axis.
- Shapes: rectangle (default), circle, ellipse and regular polygons. A
  `radius` property sets width and height together for circles and polygons
  and implies a circle when no shape is named; lists written by the plugin
  use it for those shapes.
- `LayerName` is optional on every part; a part without one is built on the
  layer that is current when the list runs.
- Actions: `extrude`, `subtract`, `union` and `curve`. Cutters are applied
  together per element; a failed boolean retries with slightly extended
  cutters, then falls back to leaving the solid and cutters for manual work.
- Diagnostics decide what gets built. An error means the part cannot be built
  correctly and is skipped, which covers a missing dimension and a zero or
  negative one. A warning means a value was resolved to a documented default
  and the part is still built, which covers an unknown color, orientation,
  shape or action and a position with the wrong number of values.
- A level 2 header with level 3 headers under it is a container: it labels the
  parts nested below and is never built itself, and whatever it declares
  becomes the defaults its children start from.
- Element families: `Boolean` and `BooleanDifference` (solids with cutters),
  `BooleanUnion` (parts unioned first), and planned surface types (`Loft`,
  `Revolve`, `Sweep1`, `Sweep2`, `Patch`, `Cap`) that produce profile curves
  with a warning.
- Layers created on demand with the given color; color values accept names
  with or without spaces, hex triplets and channel lists, falling back to the
  default color with a warning.
- Grouped parts that survive a run are placed in a Rhino group named after
  the group identifier; each run is one undo record.
- `ListModeling` command with the `Import`, `Steps` and
  `GenerateFromSelection` routes. `Steps` builds a list from command-line
  prompts; a `BooleanDifference` without cutters offers a hand-off to Rhino's
  `BooleanDifference` command or a typed cutter group. Scripted mode reads
  the import path from the command line.
- `ListModelingPanel` command and dockable **List Modeling** panel with a
  menu (Export List to File, Import File to Embedded List, Close Panel), a
  Load List button, an Embedded List editor and a Submit List button that
  runs the editor text through a temporary file.
- Embedded list text persists inside the `.3dm` as plugin document data.
- Generate from selection: orientation from the thinnest bounding-box extent,
  dimensions and position from the box, circle, ellipse and regular polygon
  detection from planar caps, one element per layer.
- Plugin scripting object (`rs.GetPlugInObject("ListModeling")`) with
  `RunFile`, `RunText`, `GenerateFromSelection`, `GetEmbeddedList`,
  `SetEmbeddedList`, `LastSummary`, `SuppressPopups` and `Version`.
- Panel controls carry stable ids and the file dialogs are thin wrappers around
  `RunListFile`, `ExportListTo` and `ImportListFrom`, so the panel can be driven
  by a script as well as by hand.
- Test suites under `tests/`: an xUnit project covering the syntax layer without
  Rhino, plus smoke, regression and UI suites that run inside Rhino. The UI
  suite drives the panel by control id, click and keystroke. Golden baselines
  cover the written form of every example, its diagnostics, and a fingerprint
  of every object each fixture builds.
- Completion popup after interactive runs, with the object count and any
  warnings or errors; script runs report to the command line only.
- Documentation: syntax reference, usage guide, roadmap for surface elements
  and tooling, and the compatibility contract for the sibling
  `rhino3d-markdown-notes` plugin.
- Build tooling: rebuilding while Rhino is running works despite the loaded
  plugin file lock; the build moves the locked binary aside and cleans the
  set-aside copies up once their Rhino has exited.
