# Run with RunPythonScript after loading the latest VisoLoom build.
# Mac fixture only. No cloud requests, API-key access, or original-document edits.
import json
import os
import tempfile
import clr
import Rhino
from System import Activator, AppDomain, Array, Object, OperatingSystem, Uri
from System.Reflection import BindingFlags

ASSEMBLY = "VisoLoom"
FLAGS = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic


def run():
    if not OperatingSystem.IsMacOS():
        raise Exception("This fixture workflow must not replace a Windows document.")
    assembly = next(a for a in AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == ASSEMBLY)
    builder = assembly.GetType("VisoLoom.Plugin.SceneBuilder")
    coordinator = assembly.GetType("VisoLoom.Plugin.CaptureCoordinator")
    if coordinator.GetMethod("Preview", FLAGS) is None:
        raise Exception("Save your work and restart Rhino with the latest VisoLoom build first.")

    def call(target, name, *args):
        return target.GetType().GetMethod(name, FLAGS).Invoke(target, Array[Object](args))

    def get(target, name):
        return target.GetType().GetProperty(name, FLAGS).GetValue(target, None)

    def set_value(target, name, value):
        target.GetType().GetProperty(name, FLAGS).SetValue(target, value, None)

    def state(doc):
        return {"objects": int(doc.Objects.Count), "layers": int(doc.Layers.Count),
                "materials": int(doc.Materials.Count), "namedViews": int(doc.NamedViews.Count),
                "modified": bool(doc.Modified), "nextUndo": int(doc.NextUndoRecordSerialNumber),
                "selection": sorted(str(o.Id) for o in doc.Objects.GetSelectedObjects(False, False)),
                "activeView": str(doc.Views.ActiveView.ActiveViewportID),
                "views": [str(builder.GetMethod("ViewSignature", FLAGS).Invoke(None, Array[Object]([v])))
                          for v in builder.GetMethod("ModelViews", FLAGS).Invoke(None, Array[Object]([doc]))]}

    original = Rhino.RhinoDoc.ActiveDoc
    before_original = state(original)
    directory = tempfile.mkdtemp(prefix="visoloom-preview-check-")
    host = os.path.dirname(clr.GetClrType(Rhino.RhinoApp).Assembly.Location)
    template = os.path.join(host, "en.lproj", "Template Files", "Small Objects - Meters.3dm")
    fixture = os.path.join(directory, "VisoLoom-preview-fixture.3dm")
    file = Rhino.FileIO.File3dm.Read(template)
    file.Objects.AddBrep(Rhino.Geometry.BoundingBox(
        Rhino.Geometry.Point3d(-2, -2, 0), Rhino.Geometry.Point3d(2, 2, 3)).ToBrep())
    assert file.Write(fixture, 8)
    file.Dispose()
    doc, already_open = Rhino.RhinoDoc.Open(fixture)
    assert doc is not None and not already_open
    view = next(v for v in builder.GetMethod("ModelViews", FLAGS).Invoke(None, Array[Object]([doc]))
                if not v.MainViewport.IsParallelProjection)
    doc.Views.ActiveView = view
    view.ActiveViewport.ZoomExtents()
    view.Redraw()
    Rhino.RhinoApp.Wait()
    original_after_opening = state(original)
    session = coordinator.GetMethod("ForDocument", FLAGS).Invoke(None, Array[Object]([doc.RuntimeSerialNumber]))
    assert not get(session, "IsBusy")
    call(session, "Configure", Uri("http://127.0.0.1:59999/"), "native-fixture-only-token-not-a-credential")
    checks = []

    def preview(label):
        view.Redraw()
        Rhino.RhinoApp.Wait()
        before = state(doc)
        call(session, "Preview", doc)
        assert get(session, "PreviewPng") is not None, str(get(session, "Status"))
        assert call(session, "PreviewIsCurrent", doc), str(get(session, "Status"))
        assert state(doc) == before, "Preview changed the Rhino fixture."
        assert get(session, "HistoryDirectory") is None
        assert get(session, "Result") is None
        assert not get(session, "IsBusy")
        checks.append({"preview": label, "stateUnchanged": True, "noHistoryWrite": True})

    preview("local")
    first = get(get(session, "Capture"), "Manifest").SceneId
    set_value(session, "UserPrompt", "Changed intent")
    assert not call(session, "PreviewIsCurrent", doc)
    call(session, "Start", doc, None, None)
    assert not get(session, "IsBusy")
    assert get(get(session, "Capture"), "Manifest").SceneId == first
    assert get(session, "HistoryDirectory") is None
    checks.append({"staleStartRejectedWithoutRecapture": True})
    set_value(session, "SelectedProvider", "openai")
    preview("openai-without-connection")
    assert get(session, "Intent").EffectivePrompt
    assert get(session, "PreviewPlan").Final == get(get(session, "Capture"), "Manifest").Frame
    set_value(session, "UserPrompt", "Another intent")
    assert not call(session, "PreviewIsCurrent", doc)
    set_value(session, "UserPrompt", "Changed intent")
    assert not call(session, "PreviewIsCurrent", doc)
    checks.append({"promptChangeExpiresReviewPermanently": True})
    preview("refreshed-openai")
    view.ActiveViewport.SetCameraLocation(view.ActiveViewport.CameraLocation + Rhino.Geometry.Vector3d.XAxis, False)
    assert not call(session, "PreviewIsCurrent", doc)
    checks.append({"cameraChangeRejected": True})
    preview("camera-adjusted")
    doc.Objects.AddPoint(Rhino.Geometry.Point3d.Origin)
    assert not call(session, "PreviewIsCurrent", doc)
    checks.append({"geometryChangeRejected": True})
    preview("scene-adjusted")
    view.ActiveViewport.DisplayMode = Rhino.Display.DisplayModeDescription.GetDisplayMode(Rhino.Display.DisplayModeDescription.RenderedId)
    assert not call(session, "PreviewIsCurrent", doc)
    preview("rendered")
    checks.append({"displayStyleChangeRejected": True})
    index = doc.NamedViews.Add("Preview check", view.MainViewport.Id)
    assert index >= 0
    named = get(session, "NamedView")
    call(named, "Select", "Preview check")
    assert not call(session, "PreviewIsCurrent", doc)
    call(session, "ShowNamedView", doc)
    preview("named-view")
    assert doc.NamedViews.Delete(index)
    assert not call(session, "PreviewIsCurrent", doc)
    checks.append({"namedViewChangeAndDeletionRejected": True})
    after_original = state(original)
    changed = [key for key in original_after_opening if original_after_opening[key] != after_original[key]]
    opening_changes = [key for key in before_original if before_original[key] != original_after_opening[key]]
    report = {"rhino": str(Rhino.RhinoApp.Version), "checks": checks,
              "originalChangedFieldsDuringChecks": changed, "originalUnchangedDuringChecks": not changed,
              "originalChangedFieldsDuringFixtureOpening": opening_changes,
              "originalBeforeOpening": before_original, "originalAfterOpening": original_after_opening,
              "originalAfterChecks": after_original}
    with open(os.path.join(directory, "report.json"), "w") as output:
        json.dump(report, output, indent=2)
    assert not changed, "Original state differs; report: " + directory
    Rhino.RhinoApp.WriteLine("VisoLoom preview checks passed. Fixture and report: " + directory)


run()
