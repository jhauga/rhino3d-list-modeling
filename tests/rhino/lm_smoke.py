# -*- coding: utf-8 -*-
"""
Smoke suite: the plugin is installed, wired up, and does the obvious thing.

These cases answer "is this build usable at all". They deliberately assert
coarse facts (the plugin loads, the commands exist, the example produces
objects) so that a failure here points at the plugin rather than at geometry.
"""

import os
import re

import Rhino

import lm_harness as h

SUITE = h.Suite("smoke", "Plugin loads, registers and performs its headline actions.")

PLUGIN_ID = "bc495df2-2c09-4174-b7a2-08371188cae7"
PANEL_ID = "09931521-f9b8-4e81-a52c-c74e309becd0"


def _guid(text):
    import System
    return System.Guid(text)


@SUITE.case("plugin is loaded from the build output")
def plugin_loaded(context):
    plugin = Rhino.PlugIns.PlugIn.Find(_guid(PLUGIN_ID))
    h.not_none(plugin, "PlugIn.Find returned nothing for the List Modeling id")

    path = Rhino.PlugIns.PlugIn.PathFromId(_guid(PLUGIN_ID))
    h.ok(str(path).lower().endswith("listmodeling.rhp"),
         "plugin path should end with ListModeling.rhp, got %s" % path)


@SUITE.case("plugin object exposes the documented scripting surface")
def scripting_surface(context):
    for member in ("RunFile", "RunText", "GenerateFromSelection",
                   "GetEmbeddedList", "SetEmbeddedList", "LastSummary",
                   "SuppressPopups", "Version"):
        h.ok(hasattr(context.lm, member), "plugin object is missing %s" % member)


@SUITE.case("assembly version matches the package manifest")
def version_matches_manifest(context):
    manifest = h.read_text(os.path.join(context.root, "manifest.yml"))
    match = re.search(r"^version:\s*(\S+)\s*$", manifest, re.MULTILINE)
    h.not_none(match, "manifest.yml has no version line")
    h.equal(str(context.lm.Version), match.group(1),
            "plugin version and manifest version have drifted apart")


@SUITE.case("both commands are registered")
def commands_registered(context):
    for name in ("ListModeling", "ListModelingPanel"):
        h.ok(Rhino.Commands.Command.IsCommand(name), "command %s is not registered" % name)


@SUITE.case("panel opens and closes")
def panel_opens_and_closes(context):
    panel_id = _guid(PANEL_ID)

    Rhino.UI.Panels.OpenPanel(panel_id)
    h.ok(Rhino.UI.Panels.IsPanelVisible(panel_id), "panel did not become visible after OpenPanel")

    Rhino.UI.Panels.ClosePanel(panel_id)
    h.ok(not Rhino.UI.Panels.IsPanelVisible(panel_id), "panel was still visible after ClosePanel")


@SUITE.case("the panel command toggles the panel")
def panel_command_toggles(context):
    panel_id = _guid(PANEL_ID)
    Rhino.UI.Panels.ClosePanel(panel_id)

    h.ok(Rhino.RhinoApp.RunScript("_ListModelingPanel", False), "ListModelingPanel command failed")
    h.ok(Rhino.UI.Panels.IsPanelVisible(panel_id), "panel command did not open the panel")

    h.ok(Rhino.RhinoApp.RunScript("_ListModelingPanel", False), "ListModelingPanel command failed")
    h.ok(not Rhino.UI.Panels.IsPanelVisible(panel_id), "panel command did not close the panel")


@SUITE.case("the shipped example builds the board, the holes and the box")
def example_builds(context):
    before = context.object_ids()
    ran = context.lm.RunFile(context.example("circuit-board.md"))
    added = context.added_since(before)

    h.ok(ran, "RunFile reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 2, "the example should leave one board and one box")

    board = h.by_name(added, "Circuit Board")
    h.equal(board["layer"], "Circuit_Board", "board layer")
    h.ok(board["solid"], "the board should be a closed solid")
    h.equal(board["faces"], 8, "a board with two round holes has 6 box faces plus 2 hole walls")

    box = h.by_name(added, "Box")
    h.equal(box["layer"], "Shipping_Box", "box layer")
    h.equal(box["faces"], 6, "the box is a plain box")


@SUITE.case("layers named in a list are created with their colors")
def layers_created(context):
    context.lm.RunFile(context.example("circuit-board.md"))
    h.equal(context.layer_color("Circuit_Board"), [0, 0, 139], "Dark Blue layer color")
    h.equal(context.layer_color("Shipping_Box"), [165, 42, 42], "Brown layer color")


@SUITE.case("running list text works the same as running a file")
def run_text_matches_run_file(context):
    before = context.object_ids()
    ran = context.lm.RunText(h.read_text(context.example("circuit-board.md")))
    added = context.added_since(before)

    h.ok(ran, "RunText reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 2, "RunText should produce the same two objects as RunFile")


@SUITE.case("the scripted command route imports a file")
def scripted_command_imports(context):
    before = context.object_ids()
    path = context.example("circuit-board.md")

    ran = Rhino.RhinoApp.RunScript('-_ListModeling _Import "%s"' % path, False)
    added = context.added_since(before)

    h.ok(ran, "the scripted -ListModeling Import route failed")
    h.equal(len(added), 2, "the scripted route should build the same two objects")


@SUITE.case("a missing file is reported rather than thrown")
def missing_file_is_reported(context):
    before = context.object_ids()
    ran = context.lm.RunFile(context.fixture("does-not-exist.md"))

    h.ok(not ran, "running a missing file should report failure")
    h.contains(context.lm.LastSummary, "not found", "summary should say the file was not found")
    h.equal(len(context.added_since(before)), 0, "nothing should be created for a missing file")


@SUITE.case("empty text is reported rather than thrown")
def empty_text_is_reported(context):
    ran = context.lm.RunText("")
    h.ok(not ran, "running empty text should report failure")
    h.contains(context.lm.LastSummary, "No '# Element:'", "summary should explain there was nothing to build")


@SUITE.case("a malformed list builds what it can and skips what it cannot")
def malformed_degrades_gracefully(context):
    before = context.object_ids()
    ran = context.lm.RunText(h.read_text(context.fixture("malformed.md")))
    added = context.added_since(before)

    h.ok(not ran, "a list with errors should report failure")
    h.contains(context.lm.LastSummary, "Errors:", "the summary should list the errors")

    # An error means the part is skipped. A warning means a value was resolved to
    # a default and the part is still built. The fixture has both kinds.
    names = sorted([item["name"] for item in added])
    h.equal(names, ["Bad Everything Else", "Good Part"],
            "only the parts whose values could all be resolved should be built")


@SUITE.case("the embedded list is readable and writable from a script")
def embedded_list_round_trips(context):
    original = context.lm.GetEmbeddedList()
    try:
        context.lm.SetEmbeddedList("# Element: Boolean\n\n## Probe\n\n* height: 1\n* width: 1\n* depth: 1\n")
        h.contains(context.lm.GetEmbeddedList(), "## Probe", "embedded list did not take the new text")
    finally:
        context.lm.SetEmbeddedList(original)


# Undo is deliberately not asserted here. Rhino does not process undo records
# created while a script is still running, so both doc.Undo() and the _Undo
# command are no-ops from inside this harness and a test would pass whatever the
# plugin did. It stays on the manual checklist in tests/README.md instead.
