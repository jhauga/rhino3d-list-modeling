# -*- coding: utf-8 -*-
"""
Progress suite: tests/progress.md builds the module that tests/progress.3dm holds.

The model is edited by hand between releases, so this suite runs only when asked
for by name (-Suite progress); the everyday suites never open it.

The model is read from disk with File3dm.Read. Nothing is opened as a document,
changed or saved. Every brep, extrusion and curve on a visible layer (parents
included) is the module; hidden layers hold backups, support geometry and
dimensions, and are left out without naming them here. The list then runs on the
test document at the model's own tolerance, and each model object is paired with
one built object on the same layer path whose bounding box agrees. Object names
are not compared: the model's come from earlier runs and hand work.
"""

import os

import Rhino

import lm_harness as h

SUITE = h.Suite("progress", "tests/progress.md builds the module as tests/progress.3dm holds it.")

BOX_TOLERANCE = 0.001
VOLUME_SHARE = 0.01

# Layers where the list's object may have another face count than the model's, named
# by the end of their path, each with the reason.
KNOWN_DIFFERENCES = [
    ("::Input Capacitor (C inp.)::shell",
     "the groove rims are filleted by hand in the model; filletEdge cannot reach edges a cut "
     "made, so the list keeps them sharp: one groove face in place of two halves and two fillets"),
]


def _known_difference(path):
    for ending, reason in KNOWN_DIFFERENCES:
        if path.endswith(ending):
            return reason
    return None


def _as_brep(geometry):
    if isinstance(geometry, Rhino.Geometry.Extrusion):
        return geometry.ToBrep(True)
    if isinstance(geometry, Rhino.Geometry.Brep):
        return geometry
    return None


def _describe(geometry, path, groups):
    """Kind, box, solidity, faces and volume of one object, read the same way for both sides."""
    box = geometry.GetBoundingBox(True)
    item = {
        "path": path,
        "box": [box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z],
        "groups": sorted(groups),
        "kind": "curve",
        "solid": False,
        "faces": 0,
        "volume": None,
    }
    brep = _as_brep(geometry)
    if brep is not None:
        item["kind"] = "brep"
        item["solid"] = bool(brep.IsSolid)
        item["faces"] = int(brep.Faces.Count)
        if brep.IsSolid:
            mass = Rhino.Geometry.VolumeMassProperties.Compute(brep)
            item["volume"] = None if mass is None else abs(mass.Volume)
    return item


def _wanted(geometry):
    return isinstance(geometry, (Rhino.Geometry.Brep, Rhino.Geometry.Extrusion, Rhino.Geometry.Curve))


# ---- The model ---------------------------------------------------------------


def _read_model(path):
    """Settings, visible objects and layer colors of the model, read without opening it."""
    model = Rhino.FileIO.File3dm.Read(path)
    if model is None:
        raise h.Failure("could not read %s" % path)

    layers = {}
    for layer in model.AllLayers:
        layers[str(layer.Id)] = layer

    def chain(layer):
        levels = []
        while layer is not None:
            levels.insert(0, layer)
            parent = str(layer.ParentLayerId)
            layer = layers.get(parent) if parent in layers else None
        return levels

    group_names = {}
    for group in model.AllGroups:
        group_names[int(group.Index)] = str(group.Name)

    objects = []
    colors = {}
    for obj in model.Objects:
        geometry = obj.Geometry
        if geometry is None or not _wanted(geometry):
            continue
        layer = model.AllLayers.FindIndex(obj.Attributes.LayerIndex)
        if layer is None:
            continue
        levels = chain(layer)
        if not all(bool(level.IsVisible) for level in levels) or not obj.Attributes.Visible:
            continue
        path = "::".join([str(level.Name) for level in levels])
        groups = [group_names.get(int(index), "?") for index in (obj.Attributes.GetGroupList() or [])]
        objects.append(_describe(geometry, path, groups))
        colors[path] = (int(layer.Color.R), int(layer.Color.G), int(layer.Color.B))

    return model.Settings, objects, colors


# ---- The comparison ---------------------------------------------------------------


def _box_gap(a, b):
    return max([abs(a["box"][i] - b["box"][i]) for i in range(6)])


def _pair(model_objects, built_objects):
    """Each model object with the built object on its layer path whose box is nearest, when it agrees."""
    pairs = []
    unpaired_model = []
    left = list(built_objects)
    for item in sorted(model_objects, key=lambda m: (m["path"], m["box"])):
        candidates = [b for b in left if b["path"] == item["path"] and b["kind"] == item["kind"]]
        candidates.sort(key=lambda b: _box_gap(item, b))
        if candidates and _box_gap(item, candidates[0]) <= BOX_TOLERANCE:
            pairs.append((item, candidates[0]))
            left.remove(candidates[0])
        else:
            unpaired_model.append(item)
    return pairs, unpaired_model, left


def _group_sizes(objects):
    sizes = {}
    for item in objects:
        for name in item["groups"]:
            sizes[name] = sizes.get(name, 0) + 1
    return sizes


def _short(path):
    """The layer path without its root, so the table stays readable."""
    parts = path.split("::")
    return "::".join(parts[1:]) if len(parts) > 1 else path


def _volume_text(value):
    return "-" if value is None else "%.6f" % value


def _write_table(context, pairs, unpaired_model, unpaired_built, summary, problems):
    lines = ["tests/progress.md against tests/progress.3dm", ""]
    lines.append("| Layer (below the root) | Kind | Model faces | List faces | Model volume | List volume | Box gap | Note |")
    lines.append("| --- | --- | --- | --- | --- | --- | --- | --- |")
    for model_item, built_item in pairs:
        note = _known_difference(model_item["path"]) if model_item["faces"] != built_item["faces"] else ""
        lines.append("| %s | %s | %d | %d | %s | %s | %.6f | %s |" % (
            _short(model_item["path"]), model_item["kind"], model_item["faces"], built_item["faces"],
            _volume_text(model_item["volume"]), _volume_text(built_item["volume"]),
            _box_gap(model_item, built_item), "known difference" if note else ""))
    for item in unpaired_model:
        lines.append("| %s | %s | %d | - | %s | - | - | no built object |" % (
            _short(item["path"]), item["kind"], item["faces"], _volume_text(item["volume"])))
    for item in unpaired_built:
        lines.append("| %s | %s | - | %d | - | %s | - | not in the model |" % (
            _short(item["path"]), item["kind"], item["faces"], _volume_text(item["volume"])))
    lines.append("")
    lines.append("Run summary:")
    lines.extend(["  " + line for line in summary.splitlines()])
    lines.append("")
    lines.append("Problems:" if problems else "Problems: none")
    lines.extend(["  " + problem for problem in problems])
    h.write_text(context.artifact_path("progress-compare.txt"), "\n".join(lines) + "\n")
    context.artifact("progress-compare.txt", "tests/progress.md compared with tests/progress.3dm")


@SUITE.case("the progress list builds the module as the model holds it")
def progress_list_builds_the_model(context):
    doc = context.doc
    settings, model_objects, model_colors = _read_model(os.path.join(context.tests_dir, "progress.3dm"))

    h.equal(str(settings.ModelUnitSystem), "Inches", "the model is in inches")
    h.equal(str(doc.ModelUnitSystem), "Inches", "the test document is in inches")
    h.ok(len(model_objects) > 0, "the model has objects on visible layers")

    previous = doc.ModelAbsoluteTolerance
    doc.ModelAbsoluteTolerance = settings.ModelAbsoluteTolerance
    try:
        before = context.object_ids()
        ran = context.lm.RunFile(os.path.join(context.tests_dir, "progress.md"))
        summary = str(context.lm.LastSummary)
        built_ids = context.added_ids_since(before)
    finally:
        doc.ModelAbsoluteTolerance = previous

    built_objects = []
    for object_id in built_ids:
        obj = doc.Objects.FindId(object_id)
        path = str(doc.Layers[obj.Attributes.LayerIndex].FullPath)
        groups = [str(doc.Groups.FindIndex(index).Name) for index in (obj.Attributes.GetGroupList() or [])]
        built_objects.append(_describe(obj.Geometry, path, groups))

    problems = []
    if not ran:
        problems.append("the list reported errors")
    if "Warnings:" in summary:
        problems.append("the list warned")

    pairs, unpaired_model, unpaired_built = _pair(model_objects, built_objects)
    for item in unpaired_model:
        problems.append("%s: a %s in the model has no built partner (box %s)"
                        % (item["path"], item["kind"], [round(v, 4) for v in item["box"]]))
    for item in unpaired_built:
        problems.append("%s: a built %s has no partner in the model; construction geometry left behind? (box %s)"
                        % (item["path"], item["kind"], [round(v, 4) for v in item["box"]]))

    for model_item, built_item in pairs:
        where = model_item["path"]
        if model_item["solid"] != built_item["solid"]:
            problems.append("%s: solid in the model %s, in the list %s" % (where, model_item["solid"], built_item["solid"]))
        if model_item["faces"] != built_item["faces"] and _known_difference(where) is None:
            problems.append("%s: %d faces in the model, %d in the list" % (where, model_item["faces"], built_item["faces"]))
        if model_item["volume"] is not None and built_item["volume"] is not None:
            share = abs(model_item["volume"] - built_item["volume"]) / max(model_item["volume"], 1e-12)
            if share > VOLUME_SHARE:
                problems.append("%s: volume %.6f in the model, %.6f in the list" % (where, model_item["volume"], built_item["volume"]))

    for path in sorted(model_colors.keys()):
        index = doc.Layers.FindByFullPath(path, -1)
        if index < 0:
            problems.append("%s: the list built no such layer" % path)
            continue
        color = doc.Layers[index].Color
        built_color = (int(color.R), int(color.G), int(color.B))
        if built_color != model_colors[path]:
            problems.append("%s: layer color %s in the model, %s in the list" % (path, model_colors[path], built_color))

    model_groups = _group_sizes(model_objects)
    built_groups = _group_sizes(built_objects)
    if model_groups != built_groups:
        problems.append("Rhino groups differ: model %s, list %s" % (sorted(model_groups.items()), sorted(built_groups.items())))

    _write_table(context, pairs, unpaired_model, unpaired_built, summary, problems)
    context.log("        %d model objects, %d built, %d paired; table in tests/artifacts/progress-compare.txt"
                % (len(model_objects), len(built_objects), len(pairs)))
    if problems:
        raise h.Failure("\n".join(problems))
