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

import Rhino

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


# ---- Shared helpers for solids with rounded faces -----------------------------


def _object(context, object_id):
    return context.doc.Objects.FindId(object_id)


def _id_named(context, ids, name):
    matches = [i for i in ids if _object(context, i).Attributes.Name == name]
    h.equal(len(matches), 1, "objects named %r" % name)
    return matches[0]


def _brep(context, object_id):
    geometry = _object(context, object_id).Geometry
    if isinstance(geometry, Rhino.Geometry.Extrusion):
        return geometry.ToBrep(True)
    return geometry


def _layer_path(context, object_id):
    layer = context.doc.Layers.FindIndex(_object(context, object_id).Attributes.LayerIndex)
    return str(layer.FullPath)


def _size3(box):
    return (box.Max.X - box.Min.X, box.Max.Y - box.Min.Y, box.Max.Z - box.Min.Z)


def _rounded_faces(brep):
    """Trimmed bounding boxes of the faces that are not planar: fillets, walls, pipes."""
    boxes = []
    for face in brep.Faces:
        if face.IsPlanar(1e-6):
            continue
        boxes.append(face.DuplicateFace(False).GetBoundingBox(True))
    return boxes


def _rim_fillets(brep, middle_z):
    """
    Radii of the rounded rims of a standing cylinder as (bottom, top), None for a
    sharp rim. A rim fillet is a quarter circle, so its height is its radius; the
    wall is the one rounded face taller than any fillet in these fixtures.
    """
    bottom = None
    top = None
    for box in _rounded_faces(brep):
        height = _size3(box)[2]
        if height >= 0.2:
            continue
        if box.Center.Z < middle_z:
            bottom = height
        else:
            top = height
    return bottom, top


def _inside(brep, x, y, z):
    return brep.IsPointInside(Rhino.Geometry.Point3d(x, y, z), 1e-6, False)


def _close_all(actual, expected, message, tolerance=1e-3):
    h.equal(sorted(actual.keys()), sorted(expected.keys()), message + ": sides found")
    for key in sorted(expected.keys()):
        h.close(actual[key], expected[key], "%s: %s" % (message, key), tolerance)


# ---- Fillet edges -------------------------------------------------------------


@SUITE.case("fillet edges round the loops and edges each select names")
def fillet_edges(context):
    ran, added, ids = _build(context, "fillets.md")
    h.ok(ran, "fillets.md reported errors: %s" % context.lm.LastSummary)
    h.ok("Warnings:" not in context.lm.LastSummary, "fillets.md warned: %s" % context.lm.LastSummary)
    h.equal(len(added), 6, "one solid per part")

    # Fillets never move a bounding box here: every flat face still reaches its
    # extreme. Face counts are the solid's own faces plus one per filleted edge,
    # plus a corner patch wherever three fillets meet.
    expected = {
        "filletCircle-TopEdge":   ([-0.805, -0.195, 0.04, -0.495, 0.115, 0.43], 4),
        "filletCircle-BothEdge":  ([-0.78, -2.2126, 0.04, -0.52, -1.9526, 0.43], 5),
        "filletCircle-BothEqual": ([0.5227, -2.1238, 0.04, 0.7827, -1.8638, 0.43], 5),
        "filletBox-topEdge":      ([-1.4358, 0.0, 0.0, 1.016, 1.446, 1.2635], 10),
        "Fillet Box - Both Edge": ([-1.2259, 0.0, 0.0, 1.2259, 1.2635, 1.446], 14),
        "filletBox-allEdges":     ([-1.3754, -2.33275, 0.0, 0.5998, -0.96525, 3.298], 26),
    }
    for name in sorted(expected.keys()):
        item = h.by_name(added, name)
        box, faces = expected[name]
        h.bbox_equal(item["bbox"], box, "%s bounding box" % name, 1e-3)
        h.equal(item["faces"], faces, "%s face count" % name)
        h.ok(item["solid"], "%s should be a closed solid" % name)

    # Loop 0 is the profile plane at the offset, loop 1 the end of the depth.
    rims = {
        "filletCircle-TopEdge": (None, 0.02),
        "filletCircle-BothEdge": (0.1, 0.02),
        "filletCircle-BothEqual": (0.02, 0.02),
    }
    for name in sorted(rims.keys()):
        bottom, top = _rim_fillets(_brep(context, _id_named(context, ids, name)), 0.235)
        want_bottom, want_top = rims[name]
        if want_bottom is None:
            h.ok(bottom is None, "%s should keep a sharp bottom rim" % name)
        else:
            h.close(bottom, want_bottom, "%s bottom rim radius" % name, 1e-3)
        h.close(top, want_top, "%s top rim radius" % name, 1e-3)

    # [.05, .05, .1, .02] on the top loop of a top part: top, right, bottom, left as
    # seen in the Top view, so +Y, +X, -Y, -X.
    sides = {}
    for box in _rounded_faces(_brep(context, _id_named(context, ids, "filletBox-topEdge"))):
        dx, dy, dz = _size3(box)
        if dx > dy:
            sides["+Y" if box.Center.Y > 0.723 else "-Y"] = dy
        else:
            sides["+X" if box.Center.X > -0.2099 else "-X"] = dx
    _close_all(sides, {"+Y": 0.05, "+X": 0.05, "-Y": 0.1, "-X": 0.02}, "filletBox-topEdge radii")

    # A front part: loop 0 is the y = 0 face, loop 1 the far face. Two values on a
    # four-edge loop are top and bottom, then right and left, as seen in the Front view.
    sides = {}
    for box in _rounded_faces(_brep(context, _id_named(context, ids, "Fillet Box - Both Edge"))):
        dx, dy, dz = _size3(box)
        loop = "front" if box.Center.Y < 0.63175 else "back"
        if dx > dz:
            side = "top" if box.Center.Z > 0.723 else "bottom"
        else:
            side = "right" if box.Center.X > 0 else "left"
        sides[loop + " " + side] = dy
    _close_all(sides, {
        "front top": 0.1, "front right": 0.2, "front bottom": 0.1, "front left": 0.2,
        "back top": 0.3, "back right": 0.15, "back bottom": 0.07, "back left": 0.03,
    }, "Fillet Box - Both Edge radii")

    # The top group claims all eight horizontal edges first (.1 below, .2 above), so
    # the front group only adds the vertical ones: .05 at the front, .1 at the back.
    horizontal = {"bottom": [], "top": []}
    vertical = {"front": [], "back": []}
    for box in _rounded_faces(_brep(context, _id_named(context, ids, "filletBox-allEdges"))):
        dx, dy, dz = _size3(box)
        if dz > 1.0:
            vertical["front" if box.Center.Y < -1.649 else "back"].append(min(dx, dy))
        elif dx > 1.0 or dy > 1.0:
            horizontal["bottom" if box.Center.Z < 1.649 else "top"].append(dz)
    for name, values, radius in (("bottom", horizontal["bottom"], 0.1), ("top", horizontal["top"], 0.2),
                                 ("front", vertical["front"], 0.05), ("back", vertical["back"], 0.1)):
        h.equal(len(values), 4 if name in ("bottom", "top") else 2, "filletBox-allEdges %s fillets" % name)
        for value in values:
            h.close(value, radius, "filletBox-allEdges %s fillet radius" % name, 1e-3)

    h.compare_golden(context, "rhino--fillets.json", added)


@SUITE.case("a fillet too large for its edges keeps the plain solid and says so")
def fillet_too_large(context):
    text = ("# Element: Boolean\n\n## Over Fillet\n\n"
            "- height: 1\n- width: 1\n- depth: 1\n- position: 700, 0\n"
            "- filletEdge:\n  - select: 1\n  - radius: 5\n- actions: extrude\n")
    before = context.object_ids()
    h.ok(context.lm.RunText(text), "a fillet failure is a warning, not an error")
    added = context.added_since(before)
    h.equal(len(added), 1, "the solid is still added")
    h.equal(added[0]["faces"], 6, "the solid keeps its six plain faces")
    h.contains(context.lm.LastSummary, "could not fillet", "the summary should explain the missing fillet")


# ---- Pipes --------------------------------------------------------------------


@SUITE.case("a pipe follows a curve part and takes its radius from the list")
def pipes(context):
    ran, added, ids = _build(context, "pipes.md")
    h.ok(ran, "pipes.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 4, "two rail curves and two pipes")

    for name, box in (("Curve 1", [2.0, -1.0, 0.0, 4.0, 1.0, 0.0]), ("Curve 2", [5.5, -0.5, 0.0, 6.5, 0.5, 0.0])):
        rail = h.by_name(added, name)
        h.equal(rail["type"], "Curve", "%s stays a curve" % name)
        h.ok(rail["closed"], "%s is a closed circle" % name)
        h.bbox_equal(rail["bbox"], box, "%s bounding box" % name)

    # A tube of radius .125 around a circle of radius 1 at (3, 0). The +X side and the
    # Z extent are exact; a pipe surface approximates the circle elsewhere, so the
    # whole box gets 0.002.
    simple = h.by_name(added, "pipe-simple")
    h.equal(simple["faces"], 1, "a pipe around a closed curve is one closed face")
    h.ok(simple["solid"], "pipe-simple should be a closed solid")
    h.close(simple["bbox"][3], 4.125, "pipe-simple max X", 1e-3)
    h.close(simple["bbox"][2], -0.125, "pipe-simple min Z", 1e-3)
    h.close(simple["bbox"][5], 0.125, "pipe-simple max Z", 1e-3)
    h.bbox_equal(simple["bbox"], [1.875, -1.125, -0.125, 4.125, 1.125, 0.125], "pipe-simple bounding box", 2e-3)

    # Stations 0: .125, 2: .06, 4: .1 around a circle of radius .5 at (6, 0). Parameter
    # 0 is the +X point, so the widest section sets max X and the Z extent; every other
    # side lies between the thinnest and the widest section.
    multi = h.by_name(added, "pipe-multi-radius")
    h.equal(multi["faces"], 1, "pipe-multi-radius face count")
    h.ok(multi["solid"], "pipe-multi-radius should be a closed solid")
    box = multi["bbox"]
    h.close(box[3], 6.625, "pipe-multi-radius max X", 1e-3)
    h.close(box[2], -0.125, "pipe-multi-radius min Z", 1e-3)
    h.close(box[5], 0.125, "pipe-multi-radius max Z", 1e-3)
    for label, reach in (("min X", 6.0 - box[0]), ("min Y", -box[1]), ("max Y", box[4])):
        h.ok(0.56 - 1e-3 <= reach <= 0.625 + 1e-3,
             "pipe-multi-radius %s reaches %s from the center, outside .56 to .625" % (label, reach))

    for name, path in (("Curve 1", "Pipe::simple"), ("pipe-simple", "Pipe::simple"),
                       ("Curve 2", "Pipe::multiple"), ("pipe-multi-radius", "Pipe::multiple")):
        h.equal(_layer_path(context, _id_named(context, ids, name)), path, "%s layer path" % name)

    h.compare_golden(context, "rhino--pipes.json", added)


@SUITE.case("a pipe can follow a named curve that is already in the model")
def pipe_along_model_curve(context):
    rail = Rhino.Geometry.Circle(Rhino.Geometry.Plane.WorldXY, 2.0).ToNurbsCurve()
    rail.Translate(Rhino.Geometry.Vector3d(500, 0, 0))
    attributes = Rhino.DocObjects.ObjectAttributes()
    attributes.Name = "Hand Rail"
    context.doc.Objects.AddCurve(rail, attributes)

    before = context.object_ids()
    h.ok(context.lm.RunText("# Element: Boolean\n\n## Hand Pipe\n\n- curve: hand rail\n- radius: .2\n- actions: pipe\n"),
         "the pipe should find the curve by name: %s" % context.lm.LastSummary)
    added = context.added_since(before)
    h.equal(len(added), 1, "only the pipe is added; the curve was already there")
    h.equal(added[0]["faces"], 1, "the pipe is one closed face")
    h.bbox_equal(added[0]["bbox"], [497.8, -2.2, -0.2, 502.2, 2.2, 0.2], "pipe around the model curve", 2e-3)


@SUITE.case("radius stations by percentage follow the length of an open curve")
def pipe_percent_stations(context):
    rail = Rhino.Geometry.LineCurve(Rhino.Geometry.Point3d(600, 0, 0), Rhino.Geometry.Point3d(610, 0, 0))
    attributes = Rhino.DocObjects.ObjectAttributes()
    attributes.Name = "Straight Rail"
    context.doc.Objects.AddCurve(rail, attributes)

    before = context.object_ids()
    text = ("# Element: Boolean\n\n## Waisted Pipe\n\n- curve: Straight Rail\n"
            "- radius:\n  - 0%: .5\n  - 50%: .2\n  - 100%: .5\n- actions: pipe\n")
    h.ok(context.lm.RunText(text), "percent stations should build: %s" % context.lm.LastSummary)
    added = context.added_since(before)
    ids = context.added_ids_since(before)
    h.equal(len(added), 1, "one pipe")
    h.equal(added[0]["faces"], 3, "an open rail gives a tube and two flat caps")
    h.ok(added[0]["solid"], "flat caps close the pipe")
    h.bbox_equal(added[0]["bbox"], [600.0, -0.5, -0.5, 610.0, 0.5, 0.5], "waisted pipe bounding box", 1e-3)

    brep = _brep(context, ids[0])
    h.ok(not _inside(brep, 605.0, 0.0, 0.3), "the middle station narrows the pipe to .2")
    h.ok(_inside(brep, 605.0, 0.0, 0.15), "the middle of the pipe is still solid inside .2")
    h.ok(_inside(brep, 600.05, 0.0, 0.45), "the ends keep their .5 radius")


@SUITE.case("a pipe whose curve cannot be found is reported and skipped")
def pipe_without_curve(context):
    before = context.object_ids()
    h.ok(not context.lm.RunText("# Element: Boolean\n\n## Lost Pipe\n\n- curve: Nowhere\n- radius: .1\n- actions: pipe\n"),
         "a missing curve is an error")
    h.equal(len(context.added_since(before)), 0, "nothing is built")
    h.contains(context.lm.LastSummary, "no curve named 'Nowhere'", "the summary should name the missing curve")


@SUITE.case("a pipe about as thin as the tolerance warns before its boolean")
def pipe_near_tolerance(context):
    radius = 2 * context.doc.ModelAbsoluteTolerance
    text = ("# Element: Boolean\n\n## Thin Rail\n\n- shape: circle\n- radius: 1\n- position: 900, 0\n- actions: curve\n\n"
            "## Thin Pipe\n\n- curve: Thin Rail\n- radius: %r\n- actions: pipe\n" % radius)
    before = context.object_ids()
    h.ok(context.lm.RunText(text), "a thin pipe is a warning, not an error")
    h.equal(len(context.added_since(before)), 2, "the rail and the thin pipe are both kept")
    h.contains(context.lm.LastSummary, "close to the model's absolute tolerance",
               "the summary should point at the tolerance")


@SUITE.case("on a closed curve the start and end stations are one point")
def pipe_closed_curve_stations(context):
    rail = ("# Element: Boolean\n\n## Loop Rail\n\n- shape: circle\n- radius: 3\n- position: %s, 0\n- actions: curve\n\n")

    before = context.object_ids()
    text = rail % "1000" + "## Waist\n\n- curve: Loop Rail\n- radius:\n  - 0%: .25\n  - 50%: .1\n  - 100%: .25\n- actions: pipe\n"
    h.ok(context.lm.RunText(text), "matching radii at 0%% and 100%% should build: %s" % context.lm.LastSummary)
    pipe = h.by_name(context.added_since(before), "Waist")
    h.equal(pipe["faces"], 1, "a pipe around a closed curve is one closed face")
    h.ok(pipe["solid"], "the waisted ring should be a closed solid")
    # The widest point is the start of the circle, on its +X side, at radius 3 + .25.
    h.close(pipe["bbox"][3], 1003.25, "max X at the .25 station", 1e-3)

    before = context.object_ids()
    text = rail % "1010" + "## Mismatch\n\n- curve: Loop Rail\n- radius:\n  - 0%: .25\n  - 100%: .1\n- actions: pipe\n"
    h.ok(not context.lm.RunText(text), "different radii at the same point are an error")
    h.contains(context.lm.LastSummary, "the start and the end (0% and 100%) are the same point",
               "the summary should explain the closed curve")
    h.equal([item["name"] for item in context.added_since(before)], ["Loop Rail"], "only the rail is built")


# ---- Nested layers ------------------------------------------------------------


@SUITE.case("all three LayerName forms land on one nested layer")
def nested_layers(context):
    ran, added, ids = _build(context, "nested-layers.md")
    h.ok(ran, "nested-layers.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 3, "one solid per form")
    for name, x in (("Path Form", 300.0), ("Quoted Form", 305.0), ("Bullet Form", 310.0)):
        item = h.by_name(added, name)
        h.bbox_equal(item["bbox"], [x - 1, -1.0, 0.0, x + 1, 1.0, 1.0], "%s bounding box" % name)
        h.equal(_layer_path(context, _id_named(context, ids, name)), "Test_Nested::Child", "%s layer path" % name)

    children = [layer for layer in context.doc.Layers
                if not layer.IsDeleted and str(layer.FullPath) == "Test_Nested::Child"]
    h.equal(len(children), 1, "the three forms share one child layer")
    parent_index = context.doc.Layers.FindByFullPath("Test_Nested", -1)
    h.ok(parent_index >= 0, "the parent layer is created at the top level")
    h.equal(str(children[0].ParentLayerId), str(context.doc.Layers[parent_index].Id), "the child sits under its parent")

    # Generation writes the path in the quoted form, and the list rebuilds on the same layer.
    context.select_only(ids)
    generated = context.lm.GenerateFromSelection()
    context.doc.Objects.UnselectAll()
    h.contains(generated, '- LayerName: "Test_Nested", "Child"', "generated list layer path")
    before = context.object_ids()
    h.ok(context.lm.RunText(generated), "the generated list should rebuild: %s" % context.lm.LastSummary)
    rebuilt = context.added_ids_since(before)
    h.equal(len(rebuilt), 3, "the rebuild adds the three solids again")
    for object_id in rebuilt:
        h.equal(_layer_path(context, object_id), "Test_Nested::Child", "rebuilt layer path")

    h.compare_golden(context, "rhino--nested-layers.json", added)


# ---- Arithmetic ---------------------------------------------------------------


@SUITE.case("arithmetic values evaluate before units convert")
def arithmetic_values(context):
    ran, added, _ = _build(context, "arithmetic.md")
    h.ok(ran, "arithmetic.md reported errors: %s" % context.lm.LastSummary)
    h.equal(len(added), 3, "one cube per part")
    # 2/2, 2in/2, (12.7 + 12.7)mm, 25.4mm * 1, (3 - 1) / 2 and -(-1) are all one inch.
    for name, x in (("Divide", 400.0), ("Units Inside", 405.0), ("Parentheses And Negatives", 410.0)):
        h.bbox_equal(h.by_name(added, name)["bbox"], [x - 0.5, -0.5, 0.0, x + 0.5, 0.5, 1.0],
                     "%s should be a one inch cube" % name)
    h.compare_golden(context, "rhino--arithmetic.json", added)


# ---- Sequenced actions --------------------------------------------------------


@SUITE.case("a pipe used as a cutter grooves a filleted part")
def sequenced_actions(context):
    # The parts are a third of an inch across and the groove pipe is .01 thick, so
    # the list runs at the small-object tolerance its reference model uses.
    previous = context.doc.ModelAbsoluteTolerance
    context.doc.ModelAbsoluteTolerance = 0.001
    try:
        ran, added, ids = _build(context, "sequenced-actions.md")
    finally:
        context.doc.ModelAbsoluteTolerance = previous

    h.ok(ran, "sequenced-actions.md reported errors: %s" % context.lm.LastSummary)
    h.ok("Warnings:" not in context.lm.LastSummary, "sequenced-actions.md warned: %s" % context.lm.LastSummary)
    h.equal(sorted([item["name"] for item in added]), ["Curve_3", "combo_fillet,pipe-subtract"],
            "the rail stays and the pipe is consumed by the cut")

    rail = h.by_name(added, "Curve_3")
    h.bbox_equal(rail["bbox"], [2.8533, 2.1569, 0.1328, 3.1533, 2.4569, 0.1328], "rail at the offset", 1e-3)

    part = h.by_name(added, "combo_fillet,pipe-subtract")
    h.ok(part["solid"], "the grooved part should be a closed solid")
    h.bbox_equal(part["bbox"], [2.8483, 2.1519, 0.04, 3.1583, 2.4619, 0.43], "the groove stays inside the outline", 1e-3)

    # Radius .155 from (3.0033, 2.3069), from z .04 to .43, the top rim rounded .02,
    # a groove of radius .01 around a circle of radius .15 at z .1328.
    brep = _brep(context, _id_named(context, ids, "combo_fillet,pipe-subtract"))
    cx, cy = 3.0033, 2.3069
    h.ok(_inside(brep, cx, cy, 0.1328), "the core is solid")
    h.ok(not _inside(brep, cx + 0.15, cy, 0.1328), "the groove is cut where the pipe ran")
    h.ok(_inside(brep, cx + 0.15, cy, 0.3), "the wall is solid above the groove")
    h.ok(not _inside(brep, cx + 0.153, cy, 0.428), "the top rim is rounded")
    h.ok(_inside(brep, cx + 0.153, cy, 0.38), "the wall is solid below the rim fillet")

    for object_id in ids:
        h.equal(_layer_path(context, object_id), "Capacitor::filletEdge-pipe_subtract", "nested layer path")

    h.compare_golden(context, "rhino--sequenced-actions.json", added)


# ---- Shipped examples -----------------------------------------------------------


@SUITE.case("the fillet and pipe example builds one grooved solid and its guide curve")
def filleted_post_example(context):
    before = context.object_ids()
    ran = context.lm.RunText(h.read_text(context.example("filleted-post.md")))
    added = context.added_since(before)
    ids = context.added_ids_since(before)
    h.ok(ran, "filleted-post.md reported errors: %s" % context.lm.LastSummary)
    h.ok("Warnings:" not in context.lm.LastSummary, "filleted-post.md warned: %s" % context.lm.LastSummary)
    h.equal(sorted([item["name"] for item in added]), ["Base Plate", "Groove Path"],
            "the post merges into the plate and the groove pipe is consumed")

    # A 3 by 3 plate .25 thick, and a post of radius .5 from .125 up to 2.625.
    solid = h.by_name(added, "Base Plate")
    h.ok(solid["solid"], "the merged part should be a closed solid")
    h.bbox_equal(solid["bbox"], [-1.5, -1.5, 0.0, 1.5, 1.5, 2.625], "plate and post together", 1e-3)

    brep = _brep(context, _id_named(context, ids, "Base Plate"))
    h.ok(_inside(brep, 0.0, 0.0, 1.5), "the post is solid at its center")
    h.ok(not _inside(brep, 0.5, 0.0, 1.5), "the groove is cut around the post")
    h.ok(_inside(brep, 0.45, 0.0, 1.0), "the post wall is solid below the groove")
    h.ok(not _inside(brep, 0.49, 0.0, 2.615), "the post's top rim is rounded")
    h.ok(not _inside(brep, 1.49, 0.0, 0.24), "the plate's top edges are rounded")
    h.ok(_inside(brep, 1.0, 1.0, 0.1), "the plate is solid away from its edges")

    h.equal(_layer_path(context, _id_named(context, ids, "Base Plate")), "Assembly::Parts", "solid layer path")
    h.equal(_layer_path(context, _id_named(context, ids, "Groove Path")), "Assembly::Guides", "guide layer path")
