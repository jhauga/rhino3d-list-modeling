# -*- coding: utf-8 -*-
"""
UI suite: the panel is driven the way a browser page is driven in a Playwright test.

Playwright itself cannot be used here. It automates web pages over the DevTools
protocol, and this panel is native Eto.Forms hosted inside Rhino, with no browser
and no DOM. The desktop equivalents (WinAppDriver, FlaUI, Appium) drive controls
from outside the process by accessibility id, which on Windows means an extra
dependency, a fragile screen-coordinate dance, and no way to read the plugin's own
state afterwards.

Eto supplies the same three things a page object needs, so the tests below read
like Playwright tests without any of that:

    Playwright                        here
    page.goto(url)                    Panels.OpenPanel(panel id)
    page.locator("#submit-list")      panel.FindChild("submit-list")
    locator.fill(text)                textArea.Text = text
    locator.press_sequentially(text)  textArea.Append(text, True)
    locator.click()                   button.PerformClick()
    expect(locator).to_be_visible()   control.Visible
    page.screenshot(path)             Graphics.CopyFromScreen

Because the driver runs inside Rhino it can also assert on what the click did to
the document and to the plugin's stored state, which an out-of-process UI driver
cannot see.

Every control the tests reach for carries a stable id declared in
ListModelingPanel.Ids, so a renamed label never breaks a test and a moved control
never silently stops being tested.
"""

import Rhino

import lm_harness as h

SUITE = h.Suite("ui", "The panel behaves when driven by control id, click and keystroke.")

PANEL_ID = "09931521-f9b8-4e81-a52c-c74e309becd0"

SAMPLE_LIST = (
    "# Element: Boolean\n"
    "\n"
    "## Panel Probe\n"
    "\n"
    "* LayerName: Test_Panel\n"
    "* orientation: top\n"
    "* height: 3\n"
    "* width:  7\n"
    "* depth:  2\n"
    "* position: 250, 0\n"
    "* actions: extrude\n")


def _guid(text):
    import System
    return System.Guid(text)


class PanelDriver(object):
    """A page object for the List Modeling panel."""

    def __init__(self, context):
        self.context = context
        panel_id = _guid(PANEL_ID)
        Rhino.UI.Panels.OpenPanel(panel_id)
        self.panel = h.not_none(
            Rhino.UI.Panels.GetPanel(panel_id, context.doc),
            "the panel is open but GetPanel returned nothing")

    # -- locators --

    def locator(self, control_id):
        """The control with this id, or a failure naming the id that is missing."""
        control = self.panel.FindChild(control_id)
        if control is None:
            raise h.Failure("no control with id %r in the panel" % control_id)
        return control

    def exists(self, control_id):
        return self.panel.FindChild(control_id) is not None

    def is_visible(self, control_id):
        return bool(self.locator(control_id).Visible)

    def text_of(self, control_id):
        return str(self.locator(control_id).Text)

    # -- actions --

    def fill(self, control_id, text):
        """Replaces the whole value, as Playwright's fill does."""
        self.locator(control_id).Text = text

    def type_at_end(self, control_id, text):
        """Appends at the caret, closer to a person typing."""
        self.locator(control_id).Append(text, True)

    def click(self, control_id):
        self.locator(control_id).PerformClick()

    def screenshot(self, name):
        """
        Saves a picture of Rhino for visual review. Best effort: the capture copies
        what is on screen, so it needs Rhino in front and a real display, and a
        failure is recorded and skipped rather than failing the case. The picture
        is an artifact, not an assertion.
        """
        path = self.context.artifact_path(name)
        _bring_rhino_to_front()
        try:
            import clr
            clr.AddReference("System.Drawing")
            from System.Drawing import Bitmap, Graphics, Size

            bounds = _screen_bounds()
            width, height = int(bounds[2]), int(bounds[3])
            bitmap = Bitmap(width, height)
            graphics = Graphics.FromImage(bitmap)
            graphics.CopyFromScreen(int(bounds[0]), int(bounds[1]), 0, 0, Size(width, height))
            graphics.Dispose()
            bitmap.Save(path)
            bitmap.Dispose()
            self.context.artifact(name, "Screen capture with the panel open")
            return True
        except Exception:
            self.context.log("        (screenshot skipped: capture is unavailable here)")
            return False


def _bring_rhino_to_front():
    """
    Raises Rhino above whatever else is on the desktop, so the capture shows the
    panel rather than the window sitting on top of it. Every step is optional.
    """
    try:
        window = Rhino.UI.RhinoEtoApp.MainWindow
        window.BringToFront()
        window.Focus()
    except Exception:
        pass
    try:
        Rhino.RhinoApp.SetFocusToMainWindow()
    except Exception:
        pass
    for _ in range(5):
        try:
            Rhino.RhinoApp.Wait()
        except Exception:
            break


def _screen_bounds():
    """
    The region to capture as (x, y, width, height). Rhino's own main window is
    preferred so another window on the same desktop cannot fill the picture; the
    whole primary screen is the fallback.
    """
    try:
        rectangle = Rhino.UI.RhinoEtoApp.MainWindow.Bounds
        if rectangle.Width > 0 and rectangle.Height > 0:
            return (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)
    except Exception:
        pass

    try:
        import clr
        clr.AddReference("System.Windows.Forms")
        from System.Windows.Forms import Screen
        rectangle = Screen.PrimaryScreen.Bounds
        return (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)
    except Exception:
        from Eto.Forms import Screen as EtoScreen
        rectangle = EtoScreen.PrimaryScreen.Bounds
        return (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height)


def _driver(context):
    if getattr(context, "_panel_driver", None) is None:
        context._panel_driver = PanelDriver(context)
    return context._panel_driver


# ---- Layout -----------------------------------------------------------------


@SUITE.case("every control in the documented layout is present and visible")
def layout_is_present(context):
    driver = _driver(context)
    for control_id in ("title", "menu", "load-list", "embedded-list-label",
                       "embedded-list", "submit-list"):
        h.ok(driver.exists(control_id), "the panel has no control with id %r" % control_id)
        h.ok(driver.is_visible(control_id), "control %r is not visible" % control_id)


@SUITE.case("the controls are labelled as the documentation says")
def labels_match_the_docs(context):
    driver = _driver(context)
    h.equal(driver.text_of("title"), "List Modeling", "panel title")
    h.equal(driver.text_of("load-list"), "Load List", "load button")
    h.equal(driver.text_of("embedded-list-label"), "Embedded List", "editor label")
    h.equal(driver.text_of("submit-list"), "Submit List", "submit button")


@SUITE.case("the menu offers exactly the three documented items")
def menu_items(context):
    driver = _driver(context)
    menu = driver.panel.BuildMenu()

    labels = []
    for item in menu.Items:
        text = getattr(item, "Text", None)
        if text:
            labels.append(str(text))

    h.equal(labels, ["Export List to File",
                     "Import File to Embedded List",
                     "Close Panel"], "menu items")


# ---- The editor and the stored list ----------------------------------------


@SUITE.case("typing into the editor updates the stored list")
def typing_updates_the_store(context):
    driver = _driver(context)
    driver.fill("embedded-list", SAMPLE_LIST)
    h.equal(context.lm.GetEmbeddedList(), SAMPLE_LIST,
            "the stored list should match what was typed")


@SUITE.case("appending at the caret keeps the stored list in step")
def appending_updates_the_store(context):
    driver = _driver(context)
    driver.fill("embedded-list", "# Element: Boolean\n")
    driver.type_at_end("embedded-list", "\n## Typed Part\n")
    h.contains(context.lm.GetEmbeddedList(), "## Typed Part",
               "text appended at the caret should reach the store")


@SUITE.case("a list set from a script appears in the editor")
def store_updates_the_editor(context):
    driver = _driver(context)
    driver.fill("embedded-list", "")
    context.lm.SetEmbeddedList(SAMPLE_LIST)
    h.equal(driver.text_of("embedded-list"), SAMPLE_LIST,
            "the editor should show what a script stored")


@SUITE.case("the editor still follows the store after the panel is closed and reopened")
def store_binding_survives_reopen(context):
    panel_id = _guid(PANEL_ID)

    Rhino.UI.Panels.ClosePanel(panel_id)
    context._panel_driver = None
    driver = _driver(context)

    context.lm.SetEmbeddedList("# Element: Boolean\n\n## After Reopen\n\n* height: 1\n* width: 1\n* depth: 1\n")
    h.contains(driver.text_of("embedded-list"), "## After Reopen",
               "a reopened panel should still refresh when the store changes")


# ---- Buttons ----------------------------------------------------------------


@SUITE.case("clicking Submit List builds the list in the editor")
def submit_builds_geometry(context):
    driver = _driver(context)
    driver.fill("embedded-list", SAMPLE_LIST)

    before = context.object_ids()
    driver.click("submit-list")
    added = context.added_since(before)

    h.equal(len(added), 1, "submitting the sample list should add one solid")
    h.equal(added[0]["name"], "Panel Probe", "the object should carry the part name")
    h.equal(added[0]["layer"], "Test_Panel", "the object should be on the layer the list names")
    h.bbox_equal(added[0]["bbox"], [246.5, -1.5, 0.0, 253.5, 1.5, 2.0], "submitted geometry")


@SUITE.case("clicking Submit List on an empty editor builds nothing and does not block")
def submit_empty_does_nothing(context):
    driver = _driver(context)
    driver.fill("embedded-list", "   \n\n")

    before = context.object_ids()
    driver.click("submit-list")

    h.equal(len(context.added_since(before)), 0, "an empty list should not create anything")


@SUITE.case("Submit List removes the temporary file it runs")
def submit_cleans_up_its_temp_file(context):
    import os
    import glob

    driver = _driver(context)
    driver.fill("embedded-list", SAMPLE_LIST)
    driver.click("submit-list")

    leftovers = glob.glob(os.path.join(os.environ.get("TEMP", "/tmp"), "ListModeling", "embedded-*.md"))
    h.equal(leftovers, [], "the temporary list file should be deleted after the run")


@SUITE.case("running a file through the panel reports and builds")
def panel_runs_a_file(context):
    driver = _driver(context)

    before = context.object_ids()
    report = driver.panel.RunListFile(context.example("circuit-board.md"))
    added = context.added_since(before)

    h.not_none(report, "RunListFile returned no report")
    h.ok(report.Succeeded, "running the example through the panel reported errors")
    h.equal(len(added), 2, "the example should add the board and the box")


# ---- Menu actions -----------------------------------------------------------


@SUITE.case("export writes the editor text and import reads it back")
def export_and_import_round_trip(context):
    driver = _driver(context)
    path = context.artifact_path("panel-export.md")

    driver.fill("embedded-list", SAMPLE_LIST)
    h.ok(driver.panel.ExportListTo(path), "ExportListTo reported failure")
    h.equal(h.read_text(path), SAMPLE_LIST, "the exported file should hold the editor text")

    driver.fill("embedded-list", "# Element: Boolean\n")
    h.ok(driver.panel.ImportListFrom(path), "ImportListFrom reported failure")
    h.equal(driver.text_of("embedded-list"), SAMPLE_LIST,
            "importing should put the file's text back in the editor")

    context.artifact("panel-export.md", "List exported through the panel menu")


@SUITE.case("importing a file that is not there is reported, not thrown")
def import_missing_file(context):
    driver = _driver(context)
    h.ok(not driver.panel.ImportListFrom(context.fixture("does-not-exist.md")),
         "importing a missing file should report failure")


# ---- Visual artifact --------------------------------------------------------


@SUITE.case("a screen capture is saved for visual review")
def capture_screenshot(context):
    driver = _driver(context)
    driver.fill("embedded-list", SAMPLE_LIST)

    # The panel docks as a tab beside the built in ones, so raise it before the
    # capture or the picture shows whichever tab happens to be selected.
    try:
        Rhino.UI.Panels.OpenPanel(_guid(PANEL_ID), True)
    except Exception:
        pass
    try:
        Rhino.RhinoApp.SetFocusToMainWindow()
        Rhino.RhinoApp.Wait()   # let the panel repaint before the capture
    except Exception:
        pass
    driver.screenshot("panel.png")
