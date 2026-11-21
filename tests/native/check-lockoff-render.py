# Run with Rhino's RunPythonScript AFTER loading the latest VisoLoom build, then
# reproduce the lock-off flow with the mouse in the twelve seconds that follow:
# set a custom size whose ratio differs from the view, click Update preview, and
# watch the primary button. This script only OBSERVES: attached to Rhino's idle
# loop, it samples the session every 250 ms including the preview fingerprint
# and the scene revision, and writes each change it sees to
# check-lockoff-render.report.json. It returns immediately, never touches the
# panel, the session or the document itself, and detaches itself when done.
import clr
import json
import os
import Rhino
from System import AppDomain, Array, Object
from System.Diagnostics import Stopwatch
from System.Reflection import BindingFlags

ASSEMBLY = "VisoLoom"
FLAGS = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
REPORT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "check-lockoff-render.report.json")
WATCH_MILLISECONDS = 12000.0

assembly = next(a for a in AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == ASSEMBLY)
coordinator = assembly.GetType("VisoLoom.Plugin.CaptureCoordinator")
scene_builder = assembly.GetType("VisoLoom.Plugin.SceneBuilder")
doc = Rhino.RhinoDoc.ActiveDoc
if doc is None:
    raise Exception("Open a Rhino model first.")
session = coordinator.GetMethod("ForDocument").Invoke(None, Array[Object]([doc.RuntimeSerialNumber]))

report = {"passed": False, "samples": []}
clock = Stopwatch()
clock.Start()
last = None


def fingerprint(active_doc):
    return coordinator.GetMethod("PreviewFingerprint", FLAGS).Invoke(session, Array[Object]([active_doc]))


def prop(name):
    return coordinator.GetProperty(name, FLAGS).GetValue(session, None)


def sample():
    active_doc = Rhino.RhinoDoc.ActiveDoc
    if active_doc is None:
        return None
    try:
        current = bool(coordinator.GetMethod("PreviewIsCurrent", FLAGS).Invoke(session, Array[Object]([active_doc])))
    except Exception as error:
        current = "error: " + str(error)
    return dict(current=current,
                linkedRatio=bool(prop("LinkedRatio")),
                customSize=str(prop("CustomSize")),
                status=str(prop("Status")),
                revision=int(scene_builder.GetProperty("Revision").GetValue(None, None)),
                fingerprint=fingerprint(active_doc))


def observe(sender, event_args):
    global last
    if clock.ElapsedMilliseconds > WATCH_MILLISECONDS:
        Rhino.RhinoApp.Idle -= observe
        report["passed"] = True
        changed = sum(1 for index in range(1, len(report["samples"]))
                      if report["samples"][index - 1].get("fingerprint") != report["samples"][index].get("fingerprint"))
        with open(REPORT, "w") as stream:
            json.dump(report, stream, indent=1)
        Rhino.RhinoApp.WriteLine("VisoLoom lock-off observer complete: " +
                                 str(len(report["samples"])) + " changes, " + str(changed) +
                                 " fingerprint changes.")
        Rhino.RhinoApp.WriteLine("Report: " + REPORT)
        return
    values = sample()
    if values is None:
        return
    if last is None or any(last.get(key) != values.get(key) for key in values):
        report["samples"].append(values)
        Rhino.RhinoApp.WriteLine("observe " + str(int(clock.ElapsedMilliseconds)) + "ms: " +
                                 json.dumps({key: value for key, value in values.items()
                                             if key not in ("fingerprint",)}))
    if values is not None:
        last = values


Rhino.RhinoApp.Idle += observe
Rhino.RhinoApp.WriteLine("VisoLoom lock-off observer attached: reproduce the free-size flow NOW (12 s).")
