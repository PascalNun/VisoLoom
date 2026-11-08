# Run through Rhino RunPythonScript after building ImageInspectionCheck.csproj.
# The default checks authored images. With manifest check="catalogue", it checks
# the current setup browser and reads the existing local model configuration.
# With check="panel", show the real panel with authored, in-memory images.
# No mode replaces the plug-in, changes a camera/model, reads credentials
# or makes a provider request. The catalogue mode must not add/remove folders.
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
    fixture = json.load(handle)
    assembly_path = fixture["path"]
    catalogue = fixture.get("check") == "catalogue"
    panel = fixture.get("check") == "panel"


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
    check_type = "VisoLoom.NativeChecks." + ("PanelCheck" if panel else "CatalogueCheck" if catalogue else "InspectionCheck")
    assembly.GetType(check_type).GetMethod("Run").Invoke(None, Array[Object]([os.path.join(directory, "ui.json")]))
    with open(os.path.join(directory, "ui.json")) as handle:
        states = json.load(handle)["states"]
    os.chmod(os.path.join(directory, "ui.json"), 0o600)
    if panel:
        report["viewerChecks"] = {
            "wideFields": any(s["width"] >= 400 and not s["verticalFields"] for s in states),
            "narrowFields": any(0 < s["width"] <= 280 and s["verticalFields"] for s in states),
            "noHorizontalOverflow": all(s["horizontalOverflow"] <= 1 for s in states if s["width"] > 0),
            "boundedPreview": all(s["imageHeight"] <= 220 for s in states),
            "symbol": all(s["symbol"] for s in states),
            "collapsedGuidance": any(s["action"] == "opened" and not s["guidanceExpanded"] for s in states),
            "recentResults": any(s["choices"] == 4 and s["compare"] and s["save"] for s in states),
            "recentThumbnails": any(s["thumbnailCount"] == 3 and s["selectedThumbnail"] == 0 for s in states),
            "earlierResultSelected": any(s["action"] == "image" and s["selectedThumbnail"] > 0 and not s["isInput"] for s in states),
            "returnedToInput": any(s["action"] == "primary" and s["isInput"] and s["primary"] == "Render view" and not s["enabled"] for s in states),
            "sizeChangeExpiresInput": any(s["action"] == "size" and s["primary"] == "Refresh preview" for s in states),
            "noSubmission": all(not s["previewSubmitted"] for s in states)
        }
    elif catalogue:
        report["viewerChecks"] = {"localShown": any(s["selected"] == 0 and s["localVisible"] and not s["cloudVisible"] for s in states),
                                  "cloudShown": any(s["selected"] == 1 and not s["localVisible"] and s["cloudVisible"] for s in states),
                                  "liveConnectionStillRequired": any("Model files accepted. Check the live connection" in s["detail"] for s in states),
                                  "narrowLayout": any(s["width"] <= 380 for s in states)}
    else:
        toggles_unchanged = True
        for previous, current in zip(states, states[1:]):
            if current["action"].startswith("Show "):
                toggles_unchanged = toggles_unchanged and all(previous[key] == current[key] for key in ["x", "y", "width", "height", "ZoomPercent"])
        wipe_moves = [(previous, current) for previous, current in zip(states, states[1:]) if current["action"] == "divider" and current["wipe"]]
        wipe_unchanged = bool(wipe_moves) and all(previous["divider"] != current["divider"] and
            all(previous[key] == current[key] for key in ["x", "y", "width", "height", "ZoomPercent"]) for previous, current in wipe_moves)
        report["viewerChecks"] = {"sourceAndResultShown": any(not s["ShowResult"] for s in states) and any(s["ShowResult"] for s in states),
                              "actualSize": any(s["action"] == "100%" and abs(s["ZoomPercent"] - 100) < 0.001 for s in states),
                              "zoom": any(s["action"] == "+" and s["ZoomPercent"] > 100 for s in states),
                              "fit": any(s["action"] == "Fit" and s["IsFit"] for s in states),
                              "toggleTransformUnchanged": toggles_unchanged,
                              "wipeKeepsSharedTransform": wipe_unchanged}
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
