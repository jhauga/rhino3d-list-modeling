# -*- coding: utf-8 -*-
"""
Entry point Rhino runs for the in-Rhino test suites.

Launched by tests/run-tests.ps1 as:

    Rhino.exe "<template>.3dm" /nosplash /netcore /runscript="-_RunPythonScript <this file>"

It loads the plugin, runs the requested suites, writes tests/artifacts/rhino-report.json
and a readable log beside it, then closes Rhino. Rhino's exit code says nothing about
the results, so the launcher reads the report.

Environment variables:
    LISTMODELING_TESTS_DIR      the tests folder (falls back to this file's location)
    LISTMODELING_RHP            the .rhp to load when it is not registered yet
    LISTMODELING_SUITES         comma separated: smoke, regression, ui (default all)
    LISTMODELING_UPDATE_GOLDEN  set to 1 to rewrite golden files instead of comparing
"""

import json
import os
import sys
import traceback

import Rhino
import System
import scriptcontext

PLUGIN_ID = "bc495df2-2c09-4174-b7a2-08371188cae7"


def tests_dir():
    from_env = os.environ.get("LISTMODELING_TESTS_DIR")
    if from_env and os.path.isdir(from_env):
        return from_env
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


TESTS = tests_dir()
ARTIFACTS = os.path.join(TESTS, "artifacts")
REPORT_PATH = os.path.join(ARTIFACTS, "rhino-report.json")
LOG_PATH = os.path.join(ARTIFACTS, "rhino-tests.log")

sys.path.insert(0, os.path.join(TESTS, "rhino"))


def fresh(path):
    handle = open(path, "w")
    handle.close()


def write_report(report):
    handle = open(REPORT_PATH, "w")
    try:
        handle.write(json.dumps(report, indent=1, sort_keys=True))
    finally:
        handle.close()


def load_plugin():
    """Returns the plugin's scripting object, loading the .rhp first if it is not registered."""
    plugin_guid = System.Guid(PLUGIN_ID)

    if Rhino.PlugIns.PlugIn.Find(plugin_guid) is None:
        rhp = os.environ.get("LISTMODELING_RHP")
        if rhp and os.path.exists(rhp):
            Rhino.PlugIns.PlugIn.LoadPlugIn(rhp)
        else:
            Rhino.PlugIns.PlugIn.LoadPlugIn(plugin_guid)

    if Rhino.PlugIns.PlugIn.Find(plugin_guid) is None:
        raise RuntimeError("the List Modeling plugin could not be loaded")

    plugin_object = Rhino.RhinoApp.GetPlugInObject("ListModeling")
    if plugin_object is None:
        raise RuntimeError("the List Modeling plugin loaded but exposed no scripting object")
    return plugin_object


def selected_suites():
    requested = os.environ.get("LISTMODELING_SUITES", "smoke,regression,ui")
    return [name.strip() for name in requested.split(",") if name.strip()]


def main():
    if not os.path.isdir(ARTIFACTS):
        os.makedirs(ARTIFACTS)
    fresh(LOG_PATH)
    if os.path.exists(REPORT_PATH):
        os.remove(REPORT_PATH)

    report = {"ok": False, "counts": {"passed": 0, "failed": 0, "error": 0},
              "total": 0, "results": [], "artifacts": []}
    plugin_object = None
    previous_suppress = None

    try:
        import lm_harness

        doc = scriptcontext.doc
        plugin_object = load_plugin()

        # A modal completion popup would wait forever with nobody to close it.
        previous_suppress = plugin_object.SuppressPopups
        plugin_object.SuppressPopups = True

        context = lm_harness.Context(doc, plugin_object, TESTS, LOG_PATH)
        context.log("List Modeling in-Rhino tests")
        context.log("plugin version : %s" % plugin_object.Version)
        context.log("plugin path    : %s" % Rhino.PlugIns.PlugIn.PathFromId(System.Guid(PLUGIN_ID)))
        context.log("document units : %s" % doc.ModelUnitSystem)
        context.log("suites         : %s" % ", ".join(selected_suites()))
        if lm_harness.updating_golden():
            context.log("golden files   : UPDATING (results are not being checked)")

        runner = lm_harness.Runner(context)

        modules = {}
        import lm_smoke
        import lm_regression
        import lm_ui
        modules["smoke"] = lm_smoke.SUITE
        modules["regression"] = lm_regression.SUITE
        modules["ui"] = lm_ui.SUITE

        for name in selected_suites():
            if name not in modules:
                context.log("unknown suite %r, skipping" % name)
                continue
            runner.run(modules[name])

        counts = runner.counts()
        context.log("")
        context.log("%d passed, %d failed, %d errored out of %d"
                    % (counts["passed"], counts["failed"], counts["error"], len(runner.results)))
        report = runner.report()

    except Exception:
        report["results"].append({
            "suite": "harness", "case": "start up",
            "status": "error", "message": traceback.format_exc()})
        report["counts"]["error"] = report["counts"]["error"] + 1
        report["total"] = len(report["results"])
        report["ok"] = False
        try:
            handle = open(LOG_PATH, "a")
            handle.write(traceback.format_exc() + "\n")
            handle.close()
        except Exception:
            pass

    finally:
        try:
            if plugin_object is not None and previous_suppress is not None:
                plugin_object.SuppressPopups = previous_suppress
        except Exception:
            pass
        write_report(report)
        try:
            scriptcontext.doc.Modified = False
        except Exception:
            pass
        Rhino.RhinoApp.Exit(False)


main()
