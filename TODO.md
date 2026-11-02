# TODO

Task-level tracking for the List Modeling plugin. Higher-level direction lives
in [docs/roadmap.md](docs/roadmap.md).

## Current

The automated suites now cover most of the first alpha. What is left is what a
test cannot reach from inside a running script, plus the routes that prompt.

- [x] Verify the `Import` route with the example list on an inch file
- [x] Verify `GenerateFromSelection` and that a generated list rebuilds the same geometry
- [x] Verify the panel: Submit List, Export, Import, Close Panel, and the editor binding
- [x] Verify layers, colors, groups, orientations, units and shapes against hand worked numbers
- [ ] Verify the `Import` route on a millimeter document
- [ ] Compare the fillet, pipe and groove fixtures by eye with the models in `.support/3d-examples/`
- [ ] Compare the global properties fixture by eye with the board in `tests/progress.3dm` (the board there sits at other heights, so compare topology only)
- [ ] Rebuild the board of `tests/progress.3dm` from a list at the model's own sizes and heights
- [ ] Verify by hand that one undo restores a solid edited through `select`
- [ ] Verify the `Steps` route end to end, including the `Command` and `Input` follow-ups for BooleanDifference
- [ ] Verify one undo step per run by hand (undo is a no-op inside the test harness)
- [ ] Verify the three file pickers by hand (Load List, Export, Import)
- [ ] Verify the embedded list saves into the `.3dm` and restores when the file reopens
- [ ] Verify `GenerateFromSelection` on meshes and on rotated geometry
- [ ] Record verification results in the roadmap

## Found Issues

Known rough edges observed in the current build.

- [ ] GenerateFromSelection describes outer extents only; holes and other subtracted features are not written as `subtract` parts
- [ ] Ellipse detection in GenerateFromSelection assumes the ellipse axes line up with the width and height axes
- [ ] Rectangles are detected by bounding box only, so a rotated rectangle is written as its enclosing box
- [ ] Circle parts written with differing height and width warn; `radius` avoids it, a `diameter` form does not exist yet
- [ ] A `LayerName` on an existing layer never updates that layer's color, even when the list gives one
- [x] Nested layer paths (`Parent::Child`) are treated as a flat layer name
- [ ] RhinoCommon 8.0.23304.9001 restores through the net48 fallback (NU1701 suppressed); evaluate a newer package with a proper net8.0 lib

## Improvements

- [ ] Optional `anchor` property (`center`, `corner`) so a position can name a corner instead of the center
- [ ] Accept `diameter` as an alternative to `radius` for circles
- [x] Per-element defaults: properties written directly under a `# Element:` header apply to every part
- [ ] Accept unit suffixes in the Steps prompts
- [ ] Steps route: prompts for `filletEdge` blocks and for pipes along a curve
- [ ] GenerateFromSelection: recognize fillets and pipes instead of writing their outer box
- [ ] Steps route: option to pick the position in the viewport instead of typing it
- [ ] Panel: syntax highlighting or at least a monospace theme that follows Rhino's dark mode
- [ ] Panel: run only the selected text of the embedded list
- [ ] Options page (default units, popup on or off, temp folder) persisted through PlugIn.Settings
- [ ] Verify and polish behavior on Rhino for Mac
- [ ] An `inherit` value that drops a header's own value so the header above it shows through (an empty value resets to the default instead)
- [ ] Group selectors with lists, ranges and counting from the end: `[0, 2]`, `[1-3]`, `[-1]`
- [ ] Written lists keep containers, repeated headers and group references instead of writing every part out in full
- [ ] Suggest the closest element type when one is misspelled (`Boolen` for `Boolean`)
- [ ] `select` by object name and across sublayers
- [ ] `select, subtract` and `select, union`, and `select` in `BooleanUnion` elements
- [ ] `filletEdge` on solids read with `select`
- [ ] GenerateFromSelection writes `square` for equal sides and `depth: through` for holes that pass all the way through
- [ ] Steps route: prompts for `square`, world coordinates, `through` and `select`
- [ ] Panel: show where each value of a part comes from (its definition, the place it is rebuilt in, the element)
- [ ] Tune the thin-part warning (now under twice the model tolerance) against more models, and say which size of the part it means

## New Ideas

- [ ] Surface elements: `Loft` from parts at increasing `offset`, then `Revolve`, `Sweep1`, `Sweep2`, `Patch`, `Cap`
- [x] Fillet edges with a loop and edge selection vocabulary (`filletEdge`)
- [ ] `chamferEdge` and `blendEdge` blocks on the `filletEdge` pattern, with a distance in place of the radius
- [ ] `select: all` in a `filletEdge` block for every edge of the part
- [ ] Transform properties: `rotation` about the depth axis, `mirror`
- [ ] Repeat syntax for patterns (`repeat: 4 along x every 10mm`)
- [ ] Variables at the top of a list (`$thickness = 3mm`) reused in parts
- [ ] JSON and XML list formats behind the same document model
- [ ] Watch a list file and rerun on save
- [ ] Positions measured from a selected solid (`z-position: top` of the solid being cut), so pockets follow the solid instead of a typed number
- [ ] Template headers that only hold values and are never built themselves, made to be reused
- [ ] A group reference with a transform (`mirror: z`, `move: 0, 0, 1`) to place a copy of a group, such as the same pads on the other face
- [ ] `include:` another list file so shared definitions live in one place
- [ ] Partial through cuts: `depth: through-first` (stop at the first solid) and `depth: to <part>`

## Documentation

- [ ] Add a screenshot of the panel and the Steps prompts to docs/usage.md
- [ ] Write docs/architecture.md (syntax layer without Rhino dependency, runner, generator, and how definitions, repeats and group references resolve)
- [ ] Document the `.3dm` persistence format (ArchivableDictionary layout and versioning rules)
- [ ] Add a troubleshooting/FAQ doc (boolean failed, layer color not applied, nothing built)
- [ ] Write a release checklist for the yak pipeline (test server first, immutable version discipline)
- [ ] Add CONTRIBUTING.md with build, test, and style notes

## Testing

The syntax layer has no Rhino dependency and can be tested with plain .NET.

### Done

- [x] Add a `tests/ListModeling.Syntax.Tests` project (parser, writer round trip, units, colors)
- [x] Add a markdownlint configuration (allow duplicate sibling headings for the list format)
- [x] Parser tests for every header form, bullet style, comment style and annotation
- [x] Unit conversion tests against an inch document
- [x] Runner tests for each orientation and shape, with hand worked bounding boxes
- [x] Generate-from-selection round trip: build, generate, rebuild, compare bounding boxes
- [x] Golden baselines for the written form, the diagnostics and the geometry of every fixture
- [x] UI suite driving the panel by control id, click and keystroke
- [x] Fixtures, goldens and in-Rhino cases for fillets, pipes, nested layers, arithmetic and action sequences
- [x] Fixtures, goldens and in-Rhino cases for nested containers, reused headers, group references, world coordinates, squares, through cuts and select

### Improve Tests

- [ ] Run all four suites in CI (GitHub Actions); the in-Rhino suites need a runner with Rhino installed
- [ ] Add a millimeter template run so unit conversion is covered in both directions
- [ ] Report timings per case so a slow suite is visible
- [ ] Capture the panel alone rather than the whole Rhino window in the UI screenshot

### New Tests

- [ ] Boolean retry and fallback paths: a difference that fails on the first try, and one that fails on both; the cutter that misses its solid no longer reaches either
- [ ] `select` on a locked, a hidden and a mesh object (all skipped), and a list that edits a selected solid run twice
- [ ] Millimeter run of the global properties fixture
- [ ] Steps route driven through scripted command input, if the prompts can be fed from a macro
- [ ] Persistence: write a `.3dm`, reopen it headless, and assert the embedded list came back
- [ ] Grouped parts that survive a run land in a Rhino group with the right name
- [ ] Large list performance, for example five hundred parts in one element

## Packaging and Release

- [x] Add a LICENSE file (MIT)
- [ ] Push the repository to a GitHub remote
- [ ] Build a yak package and smoke-test it on the package test server
- [ ] Confirm the inferred `rh8` distribution tag and install the package on a clean Rhino 8
- [ ] Coordinate the sibling `rhino3d-markdown-notes` export feature against docs/compatibility.md
