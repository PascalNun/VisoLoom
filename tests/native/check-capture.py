# Run with Rhino's RunPythonScript after loading the latest VisoLoom build.
# Mac only: Rhino opens this authored fixture in a separate document window.
# Nothing is sent to an image provider. The original document is only read.
import json
import os
import tempfile
import clr
import Rhino
from System import Activator, AppDomain, Array, Int32, InvalidOperationException, Object, OperatingSystem
from System.IO import File
from System.Reflection import BindingFlags, TargetInvocationException
from System.Runtime.InteropServices import RuntimeInformation

CUSTOM_STYLE = "Rendered_LN_PN"
BUILDER_ASSEMBLY = "VisoLoom"
FLAGS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic


def invoke(target, name, *args):
    return target.GetType().GetMethod(name).Invoke(target, Array[Object](args))


def property_value(target, name):
    return target.GetType().GetProperty(name).GetValue(target, None)


def rejects(action):
    try:
        action()
    except TargetInvocationException as error:
        if isinstance(error.InnerException, InvalidOperationException):
            return
        raise
    raise Exception("Expected the unsupported or stale source to be rejected.")


def run():
    if not OperatingSystem.IsMacOS():
        raise Exception("This fixture workflow is Mac only; it must not replace a Windows document.")
    assembly = next(a for a in AppDomain.CurrentDomain.GetAssemblies()
                    if a.GetName().Name == BUILDER_ASSEMBLY)
    builder = assembly.GetType("VisoLoom.Plugin.SceneBuilder")
    named_type = assembly.GetType("VisoLoom.Plugin.NamedViewSource")
    if builder.GetMethod("CameraSignature", FLAGS) is None or named_type is None:
        raise Exception("Save your work and restart Rhino with the latest VisoLoom build first.")
    core = next(a for a in AppDomain.CurrentDomain.GetAssemblies()
                if a.GetName().Name == "VisoLoom.Core")
    pixel_type = core.GetType("VisoLoom.Core.PixelSize")

    def scene_call(name, *args):
        return builder.GetMethod(name, FLAGS).Invoke(None, Array[Object](args))

    def state(doc):
        return {
            "modified": bool(doc.Modified),
            "undo": int(doc.CurrentUndoRecordSerialNumber),
            "nextUndo": int(doc.NextUndoRecordSerialNumber),
            "objects": int(doc.Objects.Count),
            "layers": int(doc.Layers.Count),
            "materials": int(doc.Materials.Count),
            "namedViews": int(doc.NamedViews.Count),
            "selection": sorted(str(o.Id) for o in doc.Objects.GetSelectedObjects(False, False)),
            "activeView": str(doc.Views.ActiveView.ActiveViewportID),
            "views": [str(scene_call("ViewSignature", v)) for v in scene_call("ModelViews", doc)]
        }

    original = Rhino.RhinoDoc.ActiveDoc
    original_state = state(original)
    directory = tempfile.mkdtemp(prefix="visoloom-native-check-")
    host = os.path.dirname(clr.GetClrType(Rhino.RhinoApp).Assembly.Location)
    template = os.path.join(host, "en.lproj", "Template Files", "Small Objects - Meters.3dm")
    fixture = os.path.join(directory, "VisoLoom-capture-fixture.3dm")
    file = Rhino.FileIO.File3dm.Read(template)
    for bounds in [(-5, -4, -.2, 5, 4, 0), (-2, -2, 0, -1, 2, 2),
                   (1, -2, 0, 2, 2, 2), (-3, -3, 2, 3, 3, 2.2)]:
        box = Rhino.Geometry.BoundingBox(Rhino.Geometry.Point3d(*bounds[:3]),
                                         Rhino.Geometry.Point3d(*bounds[3:]))
        file.Objects.AddBrep(box.ToBrep())
    assert file.Write(fixture, 8)
    file.Dispose()
    doc, already_open = Rhino.RhinoDoc.Open(fixture)
    assert doc is not None and not already_open
    views = list(scene_call("ModelViews", doc))
    perspective = next(v for v in views if not v.MainViewport.IsParallelProjection)
    top = next(v for v in views if v.MainViewport.Name == "Top")
    doc.Views.ActiveView = perspective
    for view in views:
        view.ActiveViewport.ZoomExtents()
    clip_id = doc.Objects.AddClippingPlane(
        Rhino.Geometry.Plane(Rhino.Geometry.Point3d.Origin, Rhino.Geometry.Vector3d.XAxis),
        12, 12, perspective.MainViewport.Id)
    assert doc.Objects.Hide(clip_id, True)

    checks = []
    modes = [Rhino.Display.DisplayModeDescription.GetDisplayMode(
             Rhino.Display.DisplayModeDescription.RenderedId)]
    custom = Rhino.Display.DisplayModeDescription.FindByName(CUSTOM_STYLE)
    if custom is not None:
        modes.append(custom)

    def capture(view, label, multiplier=1):
        # Let native drawing finish after fixture setup; production capture does not pump events.
        view.Redraw()
        Rhino.RhinoApp.Wait()
        before = state(doc)
        size = view.ActiveViewport.Size
        pixels = Activator.CreateInstance(pixel_type, Array[Object]([
            Int32(size.Width * multiplier), Int32(size.Height * multiplier)]))
        captured = scene_call("Capture", doc, view.MainViewport.Id, pixels, "authored-native-check")
        manifest = property_value(captured, "Manifest")
        assert manifest.Frame.Width == size.Width * multiplier
        assert manifest.Frame.Height == size.Height * multiplier
        assert manifest.View.ViewportId == view.MainViewport.Id
        assert manifest.View.DisplayModeId == view.ActiveViewport.DisplayMode.Id
        expected_projection = ("Parallel" if view.ActiveViewport.IsParallelProjection else
                               "TwoPointPerspective" if view.ActiveViewport.IsTwoPointPerspectiveProjection else "Perspective")
        assert str(manifest.Camera.Projection) == expected_projection
        clip = next(c for c in manifest.Clipping if c.Id == clip_id)
        assert bool(clip.AppliesToView) == (view == perspective)
        assert state(doc) == before
        File.WriteAllBytes(os.path.join(directory, label + ".png"), property_value(captured, "Png"))
        checks.append({"capture": label, "width": int(manifest.Frame.Width),
                       "height": int(manifest.Frame.Height), "stateUnchanged": True,
                       "clippingMatchesSource": True})

    for mode in modes:
        for view in (perspective, top):
            view.ActiveViewport.DisplayMode = mode
            for multiplier in (1, 2):
                capture(view, mode.EnglishName + "-" + view.MainViewport.Name + "-" + str(multiplier),
                        multiplier)

    index = doc.NamedViews.Add("VisoLoom check", perspective.MainViewport.Id)
    assert index >= 0
    named = Activator.CreateInstance(named_type, True)
    before = state(doc)
    invoke(named, "Select", "VisoLoom check")
    assert state(doc) == before
    rejects(lambda: invoke(named, "Resolve", doc))
    invoke(named, "Show", doc)
    assert invoke(named, "Resolve", doc).MainViewport.Id == perspective.MainViewport.Id
    capture(perspective, "named-view")
    # Display-style edits are allowed; a moved camera requires an explicit new source choice.
    perspective.ActiveViewport.DisplayMode = modes[0]
    assert invoke(named, "Resolve", doc).MainViewport.Id == perspective.MainViewport.Id
    perspective.ActiveViewport.SetCameraLocation(
        perspective.ActiveViewport.CameraLocation + Rhino.Geometry.Vector3d.XAxis, False)
    rejects(lambda: invoke(named, "Resolve", doc))
    invoke(named, "Show", doc)
    assert doc.NamedViews.Rename(index, "VisoLoom renamed")
    rejects(lambda: invoke(named, "Resolve", doc))
    invoke(named, "Select", "VisoLoom renamed")
    invoke(named, "Show", doc)
    assert doc.NamedViews.Delete(index)
    rejects(lambda: invoke(named, "Resolve", doc))
    checks.append({"namedSelectionReadOnly": True, "explicitRestoreWorks": True,
                   "styleChangeAllowed": True, "cameraChangeRejected": True,
                   "renameAndDeletionRejected": True})
    assert perspective.ActiveViewport.ChangeToTwoPointPerspectiveProjection(50)
    for mode in modes:
        perspective.ActiveViewport.DisplayMode = mode
        for multiplier in (1, 2):
            capture(perspective, "two-point-" + mode.EnglishName + "-" + str(multiplier), multiplier)
    index = doc.NamedViews.Add("VisoLoom two-point", perspective.MainViewport.Id)
    assert index >= 0
    before = state(doc)
    invoke(named, "Select", "VisoLoom two-point")
    assert state(doc) == before
    invoke(named, "Show", doc)
    assert invoke(named, "Resolve", doc).MainViewport.IsTwoPointPerspectiveProjection
    capture(perspective, "two-point-named-view")
    assert perspective.ActiveViewport.ChangeToPerspectiveProjection(False, 0)
    rejects(lambda: invoke(named, "Resolve", doc))
    assert doc.NamedViews.Delete(index)
    checks.append({"twoPointNamedRestoreWorks": True, "projectionChangeRejected": True})
    removed_id = top.MainViewport.Id
    assert top.Close()
    rejects(lambda: scene_call("FindView", doc, removed_id))
    assert state(original) == original_state
    report = {"rhino": str(Rhino.RhinoApp.Version),
              "runtime": str(RuntimeInformation.FrameworkDescription),
              "architecture": str(RuntimeInformation.ProcessArchitecture),
              "originalDocumentUnchanged": True, "customStyle": CUSTOM_STYLE,
              "customStyleAvailable": custom is not None, "checks": checks}
    with open(os.path.join(directory, "report.json"), "w") as output:
        json.dump(report, output, indent=2)
    Rhino.RhinoApp.WriteLine("VisoLoom native checks passed. Fixture and report: " + directory)


run()
