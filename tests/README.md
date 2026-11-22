# Tests

Four everyday suites, ordered by how much they need to run and by how early they
should fail, and one that runs on request.

| Suite | Runs in | Needs Rhino | What it proves |
| --- | --- | --- | --- |
| `unit` | `dotnet test` | no | Parsing, writing, units and colors behave, case by case |
| `smoke` | Rhino | yes | The plugin loads, registers and does its headline actions |
| `regression` | Rhino | yes | Fixture geometry matches hand worked numbers and golden files |
| `ui` | Rhino | yes | The panel behaves when driven by control id, click and keystroke |
| `progress` | Rhino, on request | yes | `tests/progress.md` builds the module that `tests/progress.3dm` holds |

## Running them

```powershell
powershell -File tests\run-tests.ps1                    # the four everyday suites
powershell -File tests\run-tests.ps1 -Suite unit        # fast, no Rhino
powershell -File tests\run-tests.ps1 -Suite rhino       # smoke, regression and ui
powershell -File tests\run-tests.ps1 -Suite ui -SkipBuild
powershell -File tests\run-tests.ps1 -Suite progress    # only when named
```

The unit suite also runs on its own through the normal .NET tooling:

```bash
dotnet test tests/ListModeling.Syntax.Tests/ListModeling.Syntax.Tests.csproj
```

The runner builds the plugin in Release, launches Rhino with an inch template
and `RHINO_PACKAGE_DIRS` pointed at the build output, and waits for
`tests/artifacts/rhino-report.json`. Rhino's exit code says nothing about the
results, so the report is what decides pass or fail. Everything written during a
run lands in `tests/artifacts/`, which is not committed.

Pass `-Template` if the default inch template is missing. The unit conversion
checks assume an inch document and say so when the assumption breaks.

## Layout

```text
tests/
  run-tests.ps1                 one entry point for every suite
  ListModeling.Syntax.Tests/    xUnit project, compiles the syntax layer directly
  fixtures/                     lists the in-Rhino suites build
  golden/                       checked-in baselines
  rhino/                        the in-Rhino suites and their harness
  artifacts/                    reports, screenshots, generated lists (ignored)
```

### Fixtures

Every fixture is parsed and written back by the unit suite, and built by the
regression suite.

| Fixture | What it exercises |
| --- | --- |
| `orientations.md` | The axis mapping of every orientation |
| `units.md` | Unit suffixes converted into the model's units |
| `shapes.md` | Rectangles, circles, ellipses, polygons and a profile curve |
| `booleans.md` | Grouped cutters and a union |
| `malformed.md` | Errors that skip a part next to warnings that do not |
| `fillets.md` | Fillet loops, per-edge radius lists and face groups |
| `pipes.md` | Pipes along curve parts, with constant and changing radii |
| `sequenced-actions.md` | A pipe used as a cutter on a filleted part |
| `nested-layers.md` | The three ways to write a nested layer |
| `arithmetic.md` | Arithmetic and units inside values |
| `references.md` | Repeated headers and group references, with the values a rebuilt part takes from its new place |
| `axis-positions.md` | `x-position`, `y-position` and `z-position` on every orientation, the nearest line winning, a negative depth, squares |
| `through-cuts.md` | `depth: through` cutters across two stacked solids and along a side axis |
| `global-properties.md` | Values set on headers several levels up, a selected solid edited in place, and pads cut, then rebuilt as solids |
| `steps.md` | Steps under one header: a curve offset into a ring, the ring cutting a blank round, a copy pasted, one copy recessed into a knob, the other moved to its own layer, and a scrap curve deleted |
| `selections.md` | Every way to select and deselect: by header, by container, by group index, by layer, a select part by its own name, each ending on a layer named after the way it was reached |

The Rhino models in `.support/3d-examples/` were drawn by hand to show what the
fillet, pipe and groove fixtures should look like. They are references to open
beside a result and compare by eye, not baselines: their sizes differ slightly
from the fixture numbers, and no test reads them.

`tests/progress.3dm` is the module model the plugin is being built toward, so it
stays here until the plugin is published. It is read only: no suite opens it as
a document, changes it or saves over it, and no everyday suite reads it at all.
The fixtures bring their own geometry instead; `global-properties.md`, for
example, draws a 2 by 1 by .1 box centered on the origin on the board layer
before its run, the way a board already in a model would be.

`tests/progress.md` is the list meant to build that model from an empty inch
document. The `progress` suite, run with `-Suite progress`, compares the two:

- it reads the model from disk with `File3dm.Read` and takes every brep,
  extrusion and curve on a visible layer, parents included, so hidden backup
  and support layers drop out without being named;
- it runs the list at the model's own absolute tolerance (0.001) and restores
  the document's tolerance afterward;
- it pairs each model object with one built object on the same layer path whose
  bounding box agrees within 0.001, then compares solidity, face count and
  volume (within 1 percent), the color of every layer that holds objects, and
  the names and sizes of Rhino groups;
- it fails on a model object with no partner and on a built object with none,
  which is how construction geometry left behind shows up;
- a short table of known differences in `lm_progress.py` lets a named layer
  differ in face count, each entry with its reason; object names are not
  compared;
- it writes the comparison to `tests/artifacts/progress-compare.txt`.

The model is edited by hand between releases, which is why neither `all` nor
`rhino` includes this suite: a model that moved ahead of its list should not
fail the everyday run. When the model gains a part, extend the list and run the
suite.

Most cases run at the inch template's absolute tolerance of 0.01. The grooved
part in `sequenced-actions.md` is cut by a pipe only 0.01 thick, and the pockets
in `global-properties.md` are shallower than the template's tolerance, so those
cases switch the document to 0.001 for the run and restore the tolerance
afterward. Separate cases check that
a pipe, or a solid, this close to the tolerance warns when it is built at 0.01.

## Why the in-Rhino suites are written this way

RhinoCommon only exists inside a running Rhino, so anything touching the runner,
the generator or the panel cannot use a normal .NET test runner. Those suites are
Python modules that Rhino executes, with `tests/rhino/lm_harness.py` supplying the
parts a runner needs: case registration, assertions that fail with a readable
message, object fingerprints and golden file handling.

The syntax layer is deliberately free of any RhinoCommon dependency, which is why
the unit project can compile it straight into the test assembly and run in
milliseconds without Rhino installed.

The Python is written for both IronPython 2.7, which `-_RunPythonScript` hosts,
and CPython 3. It stays ASCII only and avoids syntax exclusive to either version.

## The UI suite and Playwright

The request for these tests asked for Playwright, or the nearest thing available.
Playwright automates web pages over the DevTools protocol. This panel is native
Eto.Forms hosted inside Rhino, with no browser and no DOM, so Playwright cannot
attach to it at all. The desktop equivalents (WinAppDriver, FlaUI, Appium) drive
controls from outside the process, which adds a dependency, ties the tests to
screen coordinates, and still cannot read the plugin's own state afterwards.

Eto provides the three things a page object actually needs, so `lm_ui.py` reads
like a Playwright test without any of that:

| Playwright | Here |
| --- | --- |
| `page.goto(url)` | `Panels.OpenPanel(panel id)` |
| `page.locator("#submit-list")` | `panel.FindChild("submit-list")` |
| `locator.fill(text)` | `textArea.Text = text` |
| `locator.press_sequentially(text)` | `textArea.Append(text, True)` |
| `locator.click()` | `button.PerformClick()` |
| `expect(locator).to_be_visible()` | `control.Visible` |
| `page.screenshot(path)` | `Graphics.CopyFromScreen` |

Because the driver runs inside Rhino, a click can be followed by an assertion on
what it did to the document and to the stored list, which an out-of-process UI
driver cannot see.

Every control the tests reach for carries a stable id declared in
`ListModelingPanel.Ids`, so renaming a label never breaks a test and moving a
control never silently stops testing it.

`tests/artifacts/panel.png` is a picture of the Rhino window, saved for eyeballing
after a run. It is an artifact, not an assertion, and it is genuinely best effort:
the capture copies what is on screen, so a machine with no display records that it
was skipped, and the List Modeling tab is not always the selected one in the dock
it shares with the built in panels. Read the assertions, not the picture, to know
whether the panel works. Capturing the panel control on its own is on the list in
[TODO.md](../TODO.md).

## Golden files

`tests/golden/` holds three kinds of baseline:

- `*.written.md`: every fixture and example parsed and written back, so a change
  in wording, ordering or defaults shows up as a text diff.
- `*.diagnostics.txt`: the diagnostics each list produces, with levels and line
  numbers.
- `rhino--*.json`: a fingerprint of every object a fixture builds, covering name,
  layer, type, face count, solidity and bounding box.

To accept an intended change, rerun with `-UpdateGolden` (or set
`LISTMODELING_UPDATE_GOLDEN=1`), then read the diff before committing. A missing
golden is written and fails once, so a new baseline is always reviewed rather
than silently accepted.

## Errors and warnings

The fixtures encode the distinction the plugin makes, and the suites assert it
from both sides:

- An **error** means the part cannot be built correctly, so the runner skips it.
  A missing dimension, a zero dimension, and a negative height or width are
  errors. A negative depth is not: it extrudes the other way.
- A **warning** means a value could not be read but resolves to a documented
  default, so the part is still built. An unknown color, orientation, shape or
  action, and a position with the wrong number of values, are warnings.

`tests/fixtures/malformed.md` contains both kinds. The smoke suite asserts that
exactly the two parts whose values all resolve are built, and that the others
are skipped with their errors reported. Two of its parts parse cleanly and fail
only when the list runs: a select part that finds nothing of its own name, and a
sequence whose `copy` finds nothing selected.

## Manual checks

Two things the automated suites cannot reach. Both belong in a release check.

- **Undo.** Rhino does not process undo records created while a script is still
  running, so `doc.Undo()` and the `_Undo` command are both no-ops inside the
  harness and a test there would pass no matter what the plugin did. Check by
  hand that one run undoes in one step, including a run whose `select` part
  edited a solid already in the model and a run with steps.
- **File dialogs.** Load List, Export List to File and Import File to Embedded
  List each open a native OS dialog that cannot be driven in process. The suites
  cover everything behind those dialogs by calling `RunListFile`,
  `ExportListTo` and `ImportListFrom` with a path, so only the picker itself is
  unverified.
