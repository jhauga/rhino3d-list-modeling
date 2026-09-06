# -*- coding: utf-8 -*-
"""
Regression suite: the geometry a list produces does not drift.

Two kinds of check run over the same fixtures. First, explicit assertions with
numbers worked out by hand from the syntax rules, so a failure says exactly
which rule broke. Second, a golden fingerprint of every object each fixture
produces, which catches changes nobody thought to assert.

Accept intended changes by rerunning with LISTMODELING_UPDATE_GOLDEN=1 and
reviewing the diff.
"""

import lm_harness as h

SUITE = h.Suite("regression", "Geometry produced by the fixture lists stays as documented.")


def _build(context, fixture_name):
    """Runs a fixture and returns (ran_without_errors, fingerprints, added_ids)."""
    before = context.object_ids()
    ran = context.lm.RunText(h.read_text(context.fixture(fixture_name)))
    return ran, context.added_since(before), context.added_ids_since(before)


def _size(item):
    """Width, depth and height of a fingerprint's bounding box, as x, y, z extents."""
    box = item["bbox"]
    return (round(box[3] - box[0], 4), round(box[4] - box[1], 4), round(box[5] - box[2], 4))


# ---- Orientation axis mapping ----------------------------------------------


@SUITE.case("every orientation maps height, width, depth and position as documented")
def orientation_axis_mapping(context):
    ran, added, _ = _build(context, "orientations.md")
    h.ok(ran, "orientations.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 6, "one solid per orientation")

    # Each part is height 2, width 4, depth 1. The expected box comes straight
    # from the orientation table in docs/syntax.md.
    expected = {
        "Face Top":    [-2.0, -1.0, 0.0, 2.0, 1.0, 1.0],
        "Face Bottom": [8.0, -1.0, -1.0, 12.0, 1.0, 0.0],
        "Face Front":  [18.0, 0.0, -1.0, 22.0, 1.0, 1.0],
        "Face Back":   [28.0, -1.0, -1.0, 32.0, 0.0, 1.0],
        "Face Right":  [0.0, 38.0, -1.0, 1.0, 42.0, 1.0],
        "Face Left":   [-1.0, 48.0, -1.0, 0.0, 52.0, 1.0],
    }
    for name in sorted(expected.keys()):
        item = h.by_name(added, name)
        h.bbox_equal(item["bbox"], expected[name], "%s bounding box" % name)
        h.ok(item["solid"], "%s should be a closed solid" % name)
        h.equal(item["layer"], "Test_Orientations", "%s layer" % name)

    h.compare_golden(context, "rhino--orientations.json", added)


# ---- Units ------------------------------------------------------------------


@SUITE.case("unit suffixes convert into the document's model units")
def units_convert(context):
    h.equal(str(context.doc.ModelUnitSystem), "Inches",
            "the unit fixture assumes an inch document")

    ran, added, _ = _build(context, "units.md")
    h.ok(ran, "units.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 6, "one cube per unit")

    # 1in, 25.4mm, 2.54cm and 0.0254m are the same length, and a bare number is
    # already in model units, so five of the six are one inch on every side.
    for name in ("Cube Inches", "Cube Millimeters", "Cube Centimeters",
                 "Cube Meters", "Cube Model Units"):
        h.equal(_size(h.by_name(added, name)), (1.0, 1.0, 1.0),
                "%s should measure one inch on every side" % name)

    h.equal(_size(h.by_name(added, "Cube Feet")), (12.0, 12.0, 12.0),
            "one foot is twelve inches on every side")

    h.compare_golden(context, "rhino--units.json", added)


# ---- Shapes -----------------------------------------------------------------


@SUITE.case("each shape uses width and height the way its entry documents")
def shape_dimensions(context):
    ran, added, _ = _build(context, "shapes.md")
    h.ok(ran, "shapes.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 6, "one object per part, including the curve")

    plate = h.by_name(added, "Plate")
    h.equal(_size(plate), (20.0, 10.0, 2.0), "a rectangle uses width and height in full")
    h.equal(plate["faces"], 6, "a rectangular solid has six faces")

    disc = h.by_name(added, "Disc")
    h.equal(_size(disc), (10.0, 10.0, 3.0), "a circle of radius 5 is 10 across")
    h.equal(disc["faces"], 3, "a cylinder is one wall and two caps")

    oval = h.by_name(added, "Oval")
    h.equal(_size(oval), (16.0, 8.0, 1.0), "an ellipse uses width and height as its two radii")

    nut = h.by_name(added, "Nut")
    nut_size = _size(nut)
    h.close(nut_size[0], 10.3923, "a hexagon of radius 6 spans 6*sqrt(3) across the flats", 1e-3)
    h.close(nut_size[1], 12.0, "a hexagon of radius 6 spans 12 across the points", 1e-3)
    h.equal(nut["faces"], 8, "a hexagonal solid has six walls and two caps")

    stop = h.by_name(added, "Stop")
    h.equal(stop["faces"], 10, "an eight sided solid has eight walls and two caps")

    outline = h.by_name(added, "Outline")
    h.equal(outline["type"], "Curve", "a curve action adds a curve, not a solid")
    h.ok(outline["closed"], "the profile curve should be closed")
    h.equal(_size(outline), (6.0, 6.0, 0.0), "the profile is flat and 6 by 6")

    h.compare_golden(context, "rhino--shapes.json", added)


# ---- Booleans ---------------------------------------------------------------


@SUITE.case("cutters are subtracted and unions are merged")
def booleans(context):
    ran, added, _ = _build(context, "booleans.md")
    h.ok(ran, "booleans.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 2, "the drilled board and the merged union, with the cutters consumed")

    board = h.by_name(added, "Drilled Board")
    h.bbox_equal(board["bbox"], [-10.0, -5.0, 0.0, 10.0, 5.0, 2.0], "the board keeps its outline")
    h.ok(board["solid"], "the drilled board should still be a closed solid")
    h.equal(board["faces"], 8, "six box faces plus one wall per hole")
    h.equal(board["layer"], "Test_Board", "board layer")

    union = h.by_name(added, "Union Left")
    h.bbox_equal(union["bbox"], [35.0, -5.0, 0.0, 51.0, 5.0, 5.0],
                 "two boxes six apart merge into one 16 long solid")
    h.ok(union["solid"], "the union should be a closed solid")

    # A group is only created for objects that survive the run, so cutters that
    # were consumed leave no empty group behind.
    h.ok("bolts" not in context.group_names(),
         "the consumed cutters should not leave a 'bolts' group behind")

    h.compare_golden(context, "rhino--booleans.json", added)


@SUITE.case("a failed boolean keeps the parts instead of losing them")
def failed_boolean_keeps_parts(context):
    # The cutter sits far away from the board, so there is nothing to subtract
    # and the difference cannot produce a result.
    text = (
        "# Element: BooleanDifference\n\n"
        "## Lonely Board\n\n"
        "* LayerName: Test_Failed\n* orientation: top\n"
        "* height: 4\n* width: 4\n* depth: 1\n* position: 300, 300\n* actions: extrude\n\n"
        "## Far Cutter\n\n"
        "* orientation: top\n* shape: circle\n* radius: 1\n* depth: 5\n"
        "* position: 500, 500\n* offset: -2\n* actions: subtract\n")

    before = context.object_ids()
    context.lm.RunText(text)
    added = context.added_since(before)

    names = sorted([item["name"] for item in added])
    h.equal(names, ["Far Cutter", "Lonely Board"],
            "both the solid and the cutter should remain for manual work")
    h.contains(context.lm.LastSummary, "boolean difference failed",
               "the summary should explain what happened")


# ---- Generation round trip --------------------------------------------------


@SUITE.case("a generated list rebuilds the geometry it was generated from")
def generation_round_trip(context):
    _, original, original_ids = _build(context, "shapes.md")
    h.equal(len(original), 6, "the shapes fixture should produce six objects")

    context.select_only(original_ids)
    generated = context.lm.GenerateFromSelection()
    h.ok(len(generated) > 0, "GenerateFromSelection returned nothing for a live selection")
    context.doc.Objects.UnselectAll()

    before = context.object_ids()
    h.ok(context.lm.RunText(generated),
         "the generated list did not run cleanly: %s" % context.lm.LastSummary)
    rebuilt = context.added_since(before)

    h.equal(len(rebuilt), len(original), "the rebuild should produce the same number of objects")
    for index in range(len(original)):
        h.bbox_equal(rebuilt[index]["bbox"], original[index]["bbox"],
                     "rebuilt object %d (%s) should occupy the same space"
                     % (index, original[index]["name"]), 1e-3)
        h.equal(rebuilt[index]["layer"], original[index]["layer"],
                "rebuilt object %d should stay on its layer" % index)

    h.write_text(context.artifact_path("generated-from-shapes.md"), generated)
    context.artifact("generated-from-shapes.md", "List generated from the shapes fixture")


@SUITE.case("generation describes an empty selection as nothing")
def generation_with_no_selection(context):
    context.doc.Objects.UnselectAll()
    h.equal(context.lm.GenerateFromSelection(), "",
            "nothing selected should generate no list")
