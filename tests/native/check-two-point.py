# Run with Rhino's RunPythonScript while the intended two-point model view is active.
# Reads and captures that view locally; no camera edits, model writes, keys or provider calls.
import json
import math
import os
import struct
import tempfile
import Rhino
from System.Drawing.Imaging import ImageFormat
from System.IO import MemoryStream
from System.Runtime.InteropServices import RuntimeInformation


def xyz(value):
    return [float(value.X), float(value.Y), float(value.Z)]


def camera(viewport):
    frustum = viewport.GetFrustum()
    if not frustum[0]:
        raise Exception("Rhino did not return a valid frustum.")
    return {"position": xyz(viewport.CameraLocation),
            "direction": xyz(viewport.CameraDirection), "up": xyz(viewport.CameraY),
            "frustum": [float(v) for v in frustum[1:]],
            "twoPoint": bool(viewport.IsTwoPointPerspectiveProjection),
            "parallel": bool(viewport.IsParallelProjection),
            "width": int(viewport.Size.Width), "height": int(viewport.Size.Height),
            "style": str(viewport.DisplayMode.Id)}


def state(doc):
    return {"modified": bool(doc.Modified), "objects": int(doc.Objects.Count),
            "layers": int(doc.Layers.Count), "materials": int(doc.Materials.Count),
            "namedViews": int(doc.NamedViews.Count),
            "undo": int(doc.CurrentUndoRecordSerialNumber),
            "nextUndo": int(doc.NextUndoRecordSerialNumber),
            "selection": sorted(str(o.Id) for o in doc.Objects.GetSelectedObjects(False, False)),
            "activeView": str(doc.Views.ActiveView.ActiveViewportID),
            "views": [camera(v.MainViewport) for v in doc.Views.GetViewList(
                      Rhino.Display.ViewTypeFilter.Model)]}


def gcd(a, b):
    while b:
        a, b = b, a % b
    return a


def same_camera_value(a, b):
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(abs(x - y) <= 1e-12 * max(1.0, abs(x), abs(y)) for x, y in zip(a, b))
    return a == b


def run():
    doc = Rhino.RhinoDoc.ActiveDoc
    view = doc.Views.ActiveView if doc is not None else None
    if view is None or isinstance(view, Rhino.Display.RhinoPageView):
        raise Exception("Activate a two-point model viewport first.")
    viewport = view.ActiveViewport
    if not viewport.IsTwoPointPerspectiveProjection:
        raise Exception("This check needs the intended two-point perspective view.")
    if view.RealtimeDisplayMode is not None:
        raise Exception("Choose a regular Rendered or custom display style first.")
    source = camera(viewport)
    width, height = source["width"], source["height"]
    if width < 1 or height < 1 or width * height > 16777216 or max(width, height) > 16384:
        raise Exception("The visible viewport exceeds the capture limits.")
    directory = tempfile.mkdtemp(prefix="visoloom-two-point-check-")
    before = state(doc)
    # Sample the same world landmarks in the source and the native capture pipeline.
    points = [viewport.CameraTarget]
    settings = Rhino.DocObjects.ObjectEnumeratorSettings()
    settings.NormalObjects = True
    settings.LockedObjects = True
    for obj in doc.Objects.GetObjectList(settings):
        if len(points) >= 25:
            break
        bounds = obj.Geometry.GetBoundingBox(True)
        if bounds.IsValid:
            points.extend(list(bounds.GetCorners()))

    def projected(v, canvas=None):
        size = canvas or (v.Size.Width, v.Size.Height)
        if size[0] <= 0 or size[1] <= 0:
            return []
        coordinates = []
        for point in points:
            pixel = v.WorldToClient(point)
            x, y = float(pixel.X) / size[0], float(pixel.Y) / size[1]
            coordinates.append([x, y] if not (math.isnan(x) or math.isnan(y) or
                                            math.isinf(x) or math.isinf(y)) else None)
        return coordinates

    expected = projected(viewport)
    report = {"rhino": str(Rhino.RhinoApp.Version),
              "runtime": str(RuntimeInformation.FrameworkDescription),
              "source": source, "sourceLandmarks": expected, "captures": [],
              "stateUnchanged": False, "nativeFramingVerified": False}
    sizes = [(width, height)]
    if width * height * 4 <= 16777216 and max(width, height) * 2 <= 16384:
        sizes.append((width * 2, height * 2))
    else:
        units = gcd(width, height)
        if units > 1:
            smaller = max(1, units // 2)
            sizes.append((width // units * smaller, height // units * smaller))
    try:
        for capture_width, capture_height in sizes:
            observations = []

            def observe(sender, args):
                if len(observations) >= 32:
                    return
                try:
                    # Rhino retains the source Viewport.Size during offscreen capture, but
                    # WorldToClient uses the capture canvas. Normalize with that actual canvas.
                    observations.append({"camera": camera(args.Viewport),
                                         "landmarks": projected(args.Viewport, (capture_width, capture_height))})
                except Exception as error:
                    observations.append({"observationError": type(error).__name__})

            capture = Rhino.Display.ViewCapture()
            capture.Width, capture.Height = capture_width, capture_height
            capture.DrawAxes = capture.DrawGrid = capture.DrawGridAxes = False
            capture.ScaleScreenItems = capture.TransparentBackground = False
            Rhino.Display.DisplayPipeline.DrawForeground += observe
            try:
                bitmap = capture.CaptureToBitmap(view)
            finally:
                Rhino.Display.DisplayPipeline.DrawForeground -= observe
            if bitmap is None:
                raise Exception("Rhino could not capture the two-point viewport.")
            stream = MemoryStream()
            try:
                bitmap.Save(stream, ImageFormat.Png)
                png = bytearray(stream.ToArray())
            finally:
                stream.Dispose()
                bitmap.Dispose()
            assert png[:8] == bytearray(b"\x89PNG\r\n\x1a\n")
            assert struct.unpack(">II", bytes(png[16:24])) == (capture_width, capture_height)
            assert state(doc) == before, "The source state changed during capture."
            name = "two-point-{0}x{1}.png".format(capture_width, capture_height)
            with open(os.path.join(directory, name), "wb") as output:
                output.write(png)
            entry = {"file": name, "width": capture_width, "height": capture_height,
                     "framingLandmarksMatch": False, "pipeline": observations}
            report["captures"].append(entry)
            report["stateUnchanged"] = state(doc) == before
            assert observations, "The native capture pipeline supplied no framing observations."
            max_error = 0.0
            for observation in observations:
                observed = observation["camera"]
                changed = [key for key in source if key not in ("width", "height") and not same_camera_value(observed[key], source[key])]
                assert not changed, "The native capture changed: " + ", ".join(changed)
                for target, actual in zip(expected, observation["landmarks"]):
                    assert (target is None) == (actual is None), "A landmark became invalid."
                    if target is not None:
                        max_error = max(max_error, abs(target[0] - actual[0]) * capture_width,
                                        abs(target[1] - actual[1]) * capture_height)
            assert max_error <= 0.5, "The native capture shifted projected landmarks."
            entry["maxLandmarkErrorPixels"] = max_error
            entry["framingLandmarksMatch"] = True
        report["stateUnchanged"] = state(doc) == before
        report["nativeFramingVerified"] = report["stateUnchanged"] and len(report["captures"]) == len(sizes)
    finally:
        with open(os.path.join(directory, "report.json"), "w") as output:
            json.dump(report, output, indent=2)
        Rhino.RhinoApp.WriteLine("VisoLoom two-point check files: " + directory)
    Rhino.RhinoApp.WriteLine("Local two-point framing and source-state checks passed. Compare captured appearance with Rhino too.")


run()
