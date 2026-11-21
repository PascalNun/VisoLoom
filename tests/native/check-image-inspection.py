# Run through Rhino RunPythonScript after building ImageInspectionCheck.csproj.
# The default checks authored images. With manifest check="catalogue", it checks
# the current setup browser and reads the existing local model configuration.
# With check="panel", show the real panel with authored, in-memory images.
# With check="settings", check navigation only; do not connect or change folders.
# With check="saving", export authored pixels and choose only temporary folders.
# With check="model-pins", check pin persistence in an isolated headless file.
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
    settings = fixture.get("check") == "settings"
    saving = fixture.get("check") == "saving"
    model_pins = fixture.get("check") == "model-pins"
    sizes = fixture.get("check") == "sizes"


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


directory = os.path.realpath(tempfile.mkdtemp(prefix="visoloom-image-inspection-"))
os.chmod(directory, 0o700)
before = state()
report = {"before": before, "passed": False}
try:
    assembly = Assembly.LoadFrom(assembly_path)
    check_type = "VisoLoom.NativeChecks." + ("SavingCheck" if saving else "SettingsCheck" if settings else "PanelCheck" if panel or model_pins or sizes else "CatalogueCheck" if catalogue else "InspectionCheck")
    method = "RunSizes" if sizes else "RunModelPins" if model_pins else "Run"
    assembly.GetType(check_type).GetMethod(method).Invoke(None, Array[Object]([os.path.join(directory, "ui.json")]))
    with open(os.path.join(directory, "ui.json")) as handle:
        ui = json.load(handle)
        states = ui.get("states", [])
        results = ui
        if ui.get("error"):
            raise Exception(ui["error"])
    os.chmod(os.path.join(directory, "ui.json"), 0o600)
    if saving:
        exports = [s for s in states if s["action"] == "export"]
        report["viewerChecks"] = {
            "allSaveStates": set(s["selected"] for s in states) == set([0, 1, 2, 3]),
            "extraCopyWarning": any(s["selected"] == 1 and "Saved automatically.\n" in s["note"] and "unavailable" in s["note"] for s in states),
            "automaticCopyWarning": any(s["selected"] == 2 and "Saved to your chosen folder.\n" in s["note"] and "automatic image copy" in s["note"] for s in states),
            "unsavedImageRetained": any(s["selected"] == 3 and s["image"] and s["save"] and "No saved copy." in s["note"] for s in states),
            "folderChooseAndReset": any(folder is not None for folder in ui["folders"]) and ui["folders"][-1] is None,
            "exportCancelled": any(s["exportCount"] == 0 and s["note"] != "PNG exported." for s in exports),
            "exportedPng": len(ui["exports"]) == 1 and all(item["identical"] for item in ui["exports"]),
            "overwrite": sum(1 for s in exports if s["note"] == "PNG exported.") >= 2,
            "copiesMatch": ui["copiesMatch"], "missingFolderNotCreated": ui["missingFolderNotCreated"]
        }
    elif settings:
        report["viewerChecks"] = ui["checks"]
        report["viewerChecks"].update({
            "defaultWidth": any(s["action"] == "checked" and s["width"] >= 450 for s in states),
            "narrowLayout": any(s["action"] == "checked" and s["width"] <= 380 for s in states),
            "noHorizontalOverflow": all(s["horizontalOverflow"] <= 1 for s in states if s["action"] == "checked"),
            "preferencesUnchanged": ui["connectionUnchanged"] and not ui["savingChanged"]
        })
    elif panel:
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
            "recordedDisplayStyleFollowsResult": any(s["action"] == "opened" and "Rendered" in s["caption"].splitlines() for s in states) and
                any(s["action"] == "image" and s["selectedThumbnail"] > 0 and "Rendered with edges" in s["caption"].splitlines() for s in states),
            "returnedToInput": any(s["action"] == "primary" and s["isInput"] and s["primary"] == "Render view" and not s["enabled"] for s in states),
            "sizeChangeExpiresInput": any(s["action"] == "size" and s["primary"] == "Update preview" for s in states),
            "errorFeedbackRetained": any(s["action"] == "error" and s["status"] == "Fixture connection failed. Check Settings." for s in states),
            "noSubmission": all(not s["previewSubmitted"] for s in states)
        }
    elif model_pins or sizes:
        report["viewerChecks"] = results
    elif catalogue:
        report["viewerChecks"] = {
            "catalogueInline": any(s["catalogue"] and not s["existingFiles"] for s in states),
            "existingFilesInline": any(s["existingFiles"] and not s["catalogue"] for s in states),
            "narrowLayout": any(s["width"] <= 380 for s in states),
            "noHorizontalOverflow": all(s["horizontalOverflow"] <= 1 for s in states)
        }
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
    while getattr(error, "InnerException", None) is not None:
        error = error.InnerException
    report["error"] = str(error)[:2000]
    report["stack"] = str(getattr(error, "StackTrace", ""))[:4000]
    report["after"] = state()
    report["documentUnchanged"] = before == report["after"]
finally:
    path = os.path.join(directory, "report.json")
    with open(path, "w") as handle:
        json.dump(report, handle, indent=2)
    os.chmod(path, 0o600)
    Rhino.RhinoApp.WriteLine("VisoLoom inspection fixture report: " + path)
