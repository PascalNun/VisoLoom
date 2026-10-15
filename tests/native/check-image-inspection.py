# Run through Rhino RunPythonScript after building ImageInspectionCheck.csproj.
# Uses only authored images. Does not load/replace the installed plug-in, open a
# model, change a camera, read credentials, save the model or make a request.
import clr
import json
import os
import Rhino
import tempfile
from System import Array, Object
from System.Reflection import Assembly

project = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
manifest = os.path.join(project, "artifacts", "native-inspection", "assembly.json")
with open(manifest) as handle:
    assembly_path = json.load(handle)["path"]


def state():
    doc = Rhino.RhinoDoc.ActiveDoc
    return {"document": int(doc.RuntimeSerialNumber), "modified": bool(doc.Modified),
            "objects": int(doc.Objects.Count), "layers": int(doc.Layers.Count),
            "materials": int(doc.Materials.Count), "namedViews": int(doc.NamedViews.Count),
            "undo": int(doc.NextUndoRecordSerialNumber),
            "selection": sorted(str(o.Id) for o in doc.Objects.GetSelectedObjects(False, False)),
            "activeView": str(doc.Views.ActiveView.ActiveViewportID),
            "views": [{"id": str(v.ActiveViewportID), "camera": str(v.ActiveViewport.CameraLocation),
                       "direction": str(v.ActiveViewport.CameraDirection), "up": str(v.ActiveViewport.CameraUp),
                       "frustum": str(v.ActiveViewport.GetFrustum()), "size": str(v.ActiveViewport.Size),
                       "style": str(v.ActiveViewport.DisplayMode.Id),
                       "twoPoint": bool(v.ActiveViewport.IsTwoPointPerspectiveProjection)}
                      for v in doc.Views.GetViewList(True, False)]}


directory = tempfile.mkdtemp(prefix="visoloom-image-inspection-")
os.chmod(directory, 0o700)
before = state()
report = {"before": before, "passed": False}
try:
    assembly = Assembly.LoadFrom(assembly_path)
    assembly.GetType("VisoLoom.NativeChecks.InspectionCheck").GetMethod("Run").Invoke(None, Array[Object]([os.path.join(directory, "ui.json")]))
    with open(os.path.join(directory, "ui.json")) as handle:
        states = json.load(handle)["states"]
    os.chmod(os.path.join(directory, "ui.json"), 0o600)
    toggles_unchanged = True
    for previous, current in zip(states, states[1:]):
        if current["action"].startswith("Show "):
            toggles_unchanged = toggles_unchanged and all(previous[key] == current[key] for key in ["x", "y", "width", "height", "ZoomPercent"])
    report["viewerChecks"] = {"sourceAndResultShown": any(not s["ShowResult"] for s in states) and any(s["ShowResult"] for s in states),
                              "actualSize": any(s["action"] == "100%" and abs(s["ZoomPercent"] - 100) < 0.001 for s in states),
                              "zoom": any(s["action"] == "+" and s["ZoomPercent"] > 100 for s in states),
                              "fit": any(s["action"] == "Fit" and s["IsFit"] for s in states),
                              "toggleTransformUnchanged": toggles_unchanged}
    report["after"] = state()
    report["documentUnchanged"] = before == report["after"]
    report["passed"] = report["documentUnchanged"] and all(report["viewerChecks"].values())
except Exception as error:
    report["error"] = str(error)[:2000]
    report["after"] = state()
finally:
    path = os.path.join(directory, "report.json")
    with open(path, "w") as handle:
        json.dump(report, handle, indent=2)
    os.chmod(path, 0o600)
    Rhino.RhinoApp.WriteLine("VisoLoom inspection fixture report: " + path)
