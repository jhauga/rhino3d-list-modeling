# -*- coding: utf-8 -*-
"""
Test framework for the suites that have to run inside Rhino.

RhinoCommon only exists inside a running Rhino, so these suites cannot use a
normal .NET test runner. This module supplies the small parts a runner needs:
a way to register cases, assertions that fail with a readable message, object
fingerprints for comparing geometry, and golden file handling.

Written for both IronPython 2.7 (what -_RunPythonScript hosts) and CPython 3,
so it stays ASCII only and avoids syntax exclusive to either version.
"""

import json
import os
import traceback

import Rhino


# ---- Assertions -------------------------------------------------------------


class Failure(AssertionError):
    """Raised by the assertions below. Carries the message shown in the report."""
    pass


def ok(condition, message):
    if not condition:
        raise Failure(message)


def equal(actual, expected, message):
    if actual != expected:
        raise Failure("%s: expected %r, got %r" % (message, expected, actual))


def close(actual, expected, message, tolerance=1e-6):
    if abs(float(actual) - float(expected)) > tolerance:
        raise Failure("%s: expected %s, got %s (tolerance %s)"
                      % (message, expected, actual, tolerance))


def bbox_equal(actual, expected, message, tolerance=1e-6):
    """Compares two [minX, minY, minZ, maxX, maxY, maxZ] boxes."""
    equal(len(actual), 6, message + ": bounding box shape")
    for index in range(6):
        if abs(float(actual[index]) - float(expected[index])) > tolerance:
            raise Failure("%s: expected box %s, got %s (tolerance %s)"
                          % (message, expected, actual, tolerance))


def contains(haystack, needle, message):
    if needle not in haystack:
        raise Failure("%s: %r not found in %r" % (message, needle, haystack))


def not_none(value, message):
    if value is None:
        raise Failure(message + ": got None")
    return value


# ---- Suites and the runner --------------------------------------------------


class Suite(object):
    """A named list of cases. Each case is a function taking the shared context."""

    def __init__(self, name, description=""):
        self.name = name
        self.description = description
        self.cases = []

    def case(self, title):
        def register(function):
            self.cases.append((title, function))
            return function
        return register


class Runner(object):
    """Runs suites, records one result per case, and writes a JSON report."""

    def __init__(self, context):
        self.context = context
        self.results = []

    def run(self, suite):
        self.context.log("")
        self.context.log("== suite: %s ==" % suite.name)
        for title, function in suite.cases:
            self._run_case(suite.name, title, function)

    def _run_case(self, suite_name, title, function):
        record = {"suite": suite_name, "case": title, "status": "passed", "message": ""}
        try:
            function(self.context)
        except Failure:
            record["status"] = "failed"
            record["message"] = str(_current_exception())
        except Exception:
            record["status"] = "error"
            record["message"] = traceback.format_exc()
        self.results.append(record)

        marker = {"passed": "PASS", "failed": "FAIL", "error": "ERROR"}[record["status"]]
        self.context.log("%-5s %s / %s" % (marker, suite_name, title))
        if record["message"]:
            for line in record["message"].strip().splitlines():
                self.context.log("        " + line)

    def counts(self):
        summary = {"passed": 0, "failed": 0, "error": 0}
        for record in self.results:
            summary[record["status"]] = summary[record["status"]] + 1
        return summary

    def succeeded(self):
        counts = self.counts()
        return counts["failed"] == 0 and counts["error"] == 0

    def report(self):
        counts = self.counts()
        return {
            "ok": self.succeeded(),
            "counts": counts,
            "total": len(self.results),
            "results": self.results,
            "artifacts": self.context.artifacts,
        }


def _current_exception():
    import sys
    return sys.exc_info()[1]


# ---- Shared context ---------------------------------------------------------


class Context(object):
    """Everything the cases need: the document, the plugin, paths and logging."""

    def __init__(self, doc, plugin, tests_dir, log_path):
        self.doc = doc
        self.lm = plugin
        self.tests_dir = tests_dir
        self.root = os.path.dirname(tests_dir)
        self.fixtures = os.path.join(tests_dir, "fixtures")
        self.golden = os.path.join(tests_dir, "golden")
        self.artifacts_dir = os.path.join(tests_dir, "artifacts")
        self.log_path = log_path
        self.artifacts = []
        if not os.path.isdir(self.artifacts_dir):
            os.makedirs(self.artifacts_dir)

    # -- logging --

    def log(self, message):
        text = "%s" % message
        handle = open(self.log_path, "a")
        try:
            handle.write(text + "\n")
        finally:
            handle.close()
        Rhino.RhinoApp.WriteLine(text)

    def artifact(self, path, description):
        self.artifacts.append({"path": path, "description": description})

    # -- paths --

    def fixture(self, name):
        return os.path.join(self.fixtures, name)

    def example(self, name):
        return os.path.join(self.root, "examples", name)

    def artifact_path(self, name):
        return os.path.join(self.artifacts_dir, name)

    # -- document --

    def object_ids(self):
        """Ids of everything currently in the document."""
        return set([str(obj.Id) for obj in self.doc.Objects])

    def added_since(self, ids):
        """Fingerprints of objects added since the given id set, in a stable order."""
        items = []
        for obj in self.doc.Objects:
            if str(obj.Id) not in ids:
                items.append(fingerprint(self.doc, obj))
        items.sort(key=_fingerprint_key)
        return items

    def added_ids_since(self, ids):
        """Ids of objects added since the given id set, for selecting them again."""
        return [obj.Id for obj in self.doc.Objects if str(obj.Id) not in ids]

    def select_only(self, object_ids):
        self.doc.Objects.UnselectAll()
        for object_id in object_ids:
            self.doc.Objects.Select(object_id)

    def layer_color(self, name):
        layer = self.doc.Layers.FindName(name)
        if layer is None:
            return None
        return [int(layer.Color.R), int(layer.Color.G), int(layer.Color.B)]

    def group_names(self):
        return sorted([str(group.Name) for group in self.doc.Groups])


def fingerprint(doc, obj):
    """
    A comparable description of one object: what it is, where it is, and how it
    is filed. Deliberately excludes the object id, which changes on every run.
    """
    layer = doc.Layers.FindIndex(obj.Attributes.LayerIndex)
    box = obj.Geometry.GetBoundingBox(True)
    item = {
        "name": str(obj.Attributes.Name or ""),
        "layer": str(layer.Name) if layer is not None else "",
        "type": str(obj.ObjectType),
        "bbox": [_round(v) for v in (box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z)],
        "groups": int(obj.Attributes.GroupCount),
    }

    geometry = obj.Geometry
    brep = None
    if isinstance(geometry, Rhino.Geometry.Brep):
        brep = geometry
    elif isinstance(geometry, Rhino.Geometry.Extrusion):
        brep = geometry.ToBrep(True)

    if brep is not None:
        item["faces"] = int(brep.Faces.Count)
        item["solid"] = bool(brep.IsSolid)
    elif isinstance(geometry, Rhino.Geometry.Curve):
        item["closed"] = bool(geometry.IsClosed)
    return item


def _round(value):
    rounded = round(float(value), 4)
    if rounded == 0:
        return 0.0
    return rounded


def _fingerprint_key(item):
    return (item["name"], item["layer"], item["bbox"])


def bbox_of(item):
    return item["bbox"]


def by_name(items, name):
    """The single fingerprint with the given object name."""
    matches = [item for item in items if item["name"] == name]
    if len(matches) != 1:
        raise Failure("expected exactly one object named %r, found %d" % (name, len(matches)))
    return matches[0]


# ---- Golden files -----------------------------------------------------------


def updating_golden():
    return os.environ.get("LISTMODELING_UPDATE_GOLDEN") == "1"


def compare_golden(context, name, actual):
    """
    Compares a JSON-serializable value against tests/golden/<name>. Writes the
    file instead when LISTMODELING_UPDATE_GOLDEN is 1, or when it is missing (and
    then fails once, so a new baseline is always reviewed before it counts).
    """
    path = os.path.join(context.golden, name)
    text = json.dumps(actual, indent=1, sort_keys=True)

    if updating_golden() or not os.path.exists(path):
        handle = open(path, "w")
        try:
            handle.write(text + "\n")
        finally:
            handle.close()
        if not updating_golden():
            raise Failure("golden file %s did not exist and has been created; review it and rerun" % name)
        return

    handle = open(path, "r")
    try:
        expected = handle.read().strip()
    finally:
        handle.close()

    if expected != text.strip():
        raise Failure(
            "%s differs from its golden file. Rerun with LISTMODELING_UPDATE_GOLDEN=1 to accept.\n"
            "--- expected ---\n%s\n--- actual ---\n%s" % (name, expected, text))


def read_text(path):
    handle = open(path, "r")
    try:
        return handle.read()
    finally:
        handle.close()


def write_text(path, text):
    handle = open(path, "w")
    try:
        handle.write(text)
    finally:
        handle.close()
