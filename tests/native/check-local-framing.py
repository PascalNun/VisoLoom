# Run in Rhino after loading the updated VisoLoom build. Uses only a synthetic
# image: no fixture windows, document edits, credentials or network requests.
# Covers the linked fractional path and ADR 0030's lock-off safe frame: frame
# landmarks, the excluded strip, the requested export dimensions and the
# conduit's suspend/resume capture gate. The overlay's visual PNG absence is
# the hands-on Rhino walkthrough, Track 1.
import json
import os
import tempfile
import clr
import Rhino
from System import Activator, AppDomain, Array, Object
from System.Drawing import Bitmap, Color, Graphics, SolidBrush
from System.Drawing.Imaging import ImageFormat, PixelFormat
from System.IO import File, MemoryStream
from System.Reflection import BindingFlags

FLAGS = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic


def run():
    assembly = next(a for a in AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == "VisoLoom")
    core = next(a for a in AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == "VisoLoom.Core")
    protocol = next(a for a in AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == "VisoLoom.Protocol")
    if core.GetType("VisoLoom.Core.ResolutionPlan").GetProperty("Sampling") is None:
        Rhino.RhinoApp.WriteLine("Restart Rhino with the updated VisoLoom build before this check.")
        return
    builder = assembly.GetType("VisoLoom.Plugin.SceneBuilder")
    codec_type = assembly.GetType("VisoLoom.Plugin.RhinoImageProcessor")
    codec = Activator.CreateInstance(codec_type, True)
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None:
        Rhino.RhinoApp.WriteLine("Open a Rhino model before the framing check.")
        return

    def state():
        views = builder.GetMethod("ModelViews", FLAGS).Invoke(None, Array[Object]([doc]))
        return {"objects": int(doc.Objects.Count), "layers": int(doc.Layers.Count),
                "materials": int(doc.Materials.Count), "namedViews": int(doc.NamedViews.Count),
                "modified": bool(doc.Modified), "nextUndo": int(doc.NextUndoRecordSerialNumber),
                "selection": sorted(str(o.Id) for o in doc.Objects.GetSelectedObjects(False, False)),
                "activeView": str(doc.Views.ActiveView.ActiveViewportID),
                "views": [str(builder.GetMethod("ViewSignature", FLAGS).Invoke(None, Array[Object]([v]))) for v in views]}

    def call(name, *args):
        return codec_type.GetMethod(name, FLAGS).Invoke(codec, Array[Object](args))

    directory = tempfile.mkdtemp(prefix="visoloom-local-framing-")
    os.chmod(directory, 0o700)
    report = {"passed": False, "checks": []}
    before = state()
    source = graphics = stream = None
    try:
        size_type = core.GetType("VisoLoom.Core.PixelSize")
        from System import Int32
        size = Activator.CreateInstance(size_type, Array[Object]([Int32(2049), Int32(1397)]))
        models_type = protocol.GetType("VisoLoom.Protocol.ManagedModels")
        definition = models_type.GetMethod("Find").Invoke(None, Array[Object](["flux2-klein-4b"]))
        model = definition.GetType().GetMethod("Create").Invoke(definition, Array[Object](["native-framing-check"]))
        plan = core.GetType("VisoLoom.Core.ResolutionPlanner").GetMethod("Plan").Invoke(None, Array[Object]([size, size, model.Resolution]))
        colours = [Color.Red, Color.Blue, Color.Lime]
        points = [(60, 60), (1988, 1336), (1024, 698)]
        source = Bitmap(2049, 1397, PixelFormat.Format24bppRgb)
        graphics = Graphics.FromImage(source)
        graphics.Clear(Color.White)
        for colour, point in zip(colours, points):
            brush = SolidBrush(colour)
            try:
                graphics.FillRectangle(brush, point[0] - 10, point[1] - 10, 20, 20)
            finally:
                brush.Dispose()
        graphics.Dispose()
        graphics = None
        stream = MemoryStream()
        source.Save(stream, ImageFormat.Png)
        prepared = call("PrepareReference", stream.ToArray(), plan)
        restored = call("NormalizeResult", prepared, plan)
        for phase, data in [("prepared", prepared), ("restored", restored)]:
            data_stream = MemoryStream(data)
            image = Bitmap(data_stream)
            try:
                for index, (colour, point) in enumerate(zip(colours, points)):
                    expected = plan.ToNative(float(point[0]), float(point[1])) if phase == "prepared" else None
                    ex, ey = (float(expected.Item1), float(expected.Item2)) if expected is not None else point
                    sx = sy = 0.0
                    count = 0
                    for y in range(max(0, int(ey) - 15), min(image.Height, int(ey) + 16)):
                        for x in range(max(0, int(ex) - 15), min(image.Width, int(ex) + 16)):
                            pixel = image.GetPixel(x, y)
                            if abs(pixel.R - colour.R) + abs(pixel.G - colour.G) + abs(pixel.B - colour.B) < 80:
                                sx += x + 0.5
                                sy += y + 0.5
                                count += 1
                    if count == 0:
                        raise Exception("A framing landmark disappeared.")
                    dx, dy = sx / count - ex, sy / count - ey
                    report["checks"].append({"phase": phase, "point": index, "dx": dx, "dy": dy})
                    if max(abs(dx), abs(dy)) > 1:
                        raise Exception("A framing landmark drifted by more than one pixel.")
                File.WriteAllBytes(os.path.join(directory, phase + ".png"), data)
                os.chmod(os.path.join(directory, phase + ".png"), 0o600)
            finally:
                image.Dispose()
                data_stream.Dispose()

        # ADR 0030's lock-off path: a freely chosen ratio plans through the
        # centered safe frame. Landmarks inside the frame map into the canvas,
        # the excluded strip stays out, the export keeps the requested
        # dimensions, and the conduit reports its capture gate honestly.
        def located(image, colour, ex, ey):
            sx = sy = 0.0
            count = 0
            for y in range(max(0, int(ey) - 15), min(image.Height, int(ey) + 16)):
                for x in range(max(0, int(ex) - 15), min(image.Width, int(ex) + 16)):
                    pixel = image.GetPixel(x, y)
                    if abs(pixel.R - colour.R) + abs(pixel.G - colour.G) + abs(pixel.B - colour.B) < 80:
                        sx += x + 0.5
                        sy += y + 0.5
                        count += 1
            if count == 0:
                raise Exception("A free-frame landmark disappeared.")
            return max(abs(sx / count - ex), abs(sy / count - ey))

        free_size = Activator.CreateInstance(size_type, Array[Object]([Int32(1024), Int32(768)]))
        rectangle = core.GetType("VisoLoom.Core.SafeFrame").GetMethod("LargestCentered").Invoke(
            None, Array[Object]([size, free_size]))
        frame_x, frame_y = float(rectangle.X), float(rectangle.Y)
        frame_w, frame_h = float(rectangle.Width), float(rectangle.Height)
        free_plan = core.GetType("VisoLoom.Core.ResolutionPlanner").GetMethod("PlanFreeFrame").Invoke(
            None, Array[Object]([size, free_size, model.Resolution]))
        magenta = Color.FromArgb(255, 255, 0, 255)
        markers = [(Color.Orange, 0.2, 0.3), (Color.Blue, 0.6, 0.8), (Color.Lime, 0.8, 0.2)]
        free_source = free_graphics = free_stream = None
        try:
            free_source = Bitmap(2049, 1397, PixelFormat.Format24bppRgb)
            free_graphics = Graphics.FromImage(free_source)
            free_graphics.Clear(Color.White)
            strip = SolidBrush(magenta)
            try:
                free_graphics.FillRectangle(strip, 0, 0, max(1, int(frame_x)), 1397)
            finally:
                strip.Dispose()
            for colour, part_x, part_y in markers:
                brush = SolidBrush(colour)
                try:
                    free_graphics.FillRectangle(brush, int(frame_x + frame_w * part_x) - 10,
                                                int(frame_y + frame_h * part_y) - 10, 20, 20)
                finally:
                    brush.Dispose()
            free_graphics.Dispose()
            free_graphics = None
            free_stream = MemoryStream()
            free_source.Save(free_stream, ImageFormat.Png)
            for phase, data in [("free-prepared", call("PrepareReference", free_stream.ToArray(), free_plan)),
                                ("free-restored", call("NormalizeResult",
                                                       call("PrepareReference", free_stream.ToArray(), free_plan), free_plan))]:
                phase_stream = MemoryStream(data)
                image = Bitmap(phase_stream)
                try:
                    if phase == "free-restored" and (image.Width != 1024 or image.Height != 768):
                        raise Exception("The free-frame export did not keep the requested dimensions.")
                    for index, (colour, part_x, part_y) in enumerate(markers):
                        expected = free_plan.ToNative(frame_w * part_x, frame_h * part_y) \
                            if phase == "free-prepared" else (part_x * 1024.0, part_y * 768.0)
                        ex, ey = (float(expected.Item1), float(expected.Item2)) if phase == "free-prepared" else expected
                        drift = located(image, colour, ex, ey)
                        report["checks"].append({"phase": phase, "point": index, "drift": drift})
                        if drift > 1:
                            raise Exception("A free-frame landmark drifted by more than one pixel.")
                    leaked = 0
                    for y in range(0, image.Height, 13):
                        for x in range(0, image.Width, 13):
                            pixel = image.GetPixel(x, y)
                            if abs(pixel.R - magenta.R) < 40 and pixel.G < 40 and abs(pixel.B - magenta.B) < 40:
                                leaked += 1
                    if leaked:
                        raise Exception("The excluded region reached the " + phase + " image.")
                    File.WriteAllBytes(os.path.join(directory, phase + ".png"), data)
                    os.chmod(os.path.join(directory, phase + ".png"), 0o600)
                finally:
                    image.Dispose()
                    phase_stream.Dispose()
        finally:
            for item in [free_graphics, free_stream, free_source]:
                if item is not None:
                    item.Dispose()

        # The conduit's capture gate: on with a chosen ratio, suspended around
        # the capture call, off when the panel releases the ratio.
        overlay_type = assembly.GetType("VisoLoom.Plugin.FrameOverlay")
        if overlay_type is None:
            raise Exception("The frame overlay is missing from the plug-in build.")
        overlay = Activator.CreateInstance(overlay_type, True)
        is_shown = overlay_type.GetProperty("IsShown").GetValue
        is_suspended = overlay_type.GetProperty("IsSuspended").GetValue
        overlay_update = overlay_type.GetMethod("Update")
        view_id = doc.Views.ActiveView.ActiveViewportID
        overlay_update.Invoke(overlay, Array[Object]([view_id, free_size]))
        if not bool(is_shown(overlay)):
            raise Exception("The frame overlay did not report its on state.")
        overlay_type.GetMethod("Suspend").Invoke(overlay, Array[Object]([]))
        if bool(is_shown(overlay)) or not bool(is_suspended(overlay)):
            raise Exception("The frame overlay did not suspend for the capture call.")
        overlay_type.GetMethod("Resume").Invoke(overlay, Array[Object]([]))
        if not bool(is_shown(overlay)):
            raise Exception("The frame overlay did not resume after the capture call.")
        overlay_update.Invoke(overlay, Array[Object]([view_id, None]))
        if bool(is_shown(overlay)):
            raise Exception("The frame overlay stayed on after the panel released the ratio.")
        report["checks"].append({"phase": "overlay", "gate": "suspend-resume"})
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)[:2000]
    finally:
        for item in [graphics, stream, source]:
            if item is not None:
                item.Dispose()
        report["before"] = before
        report["after"] = state()
        report["documentUnchanged"] = report["before"] == report["after"]
        report["passed"] = report["passed"] and report["documentUnchanged"]
        path = os.path.join(directory, "report.json")
        with open(path, "w") as output:
            json.dump(report, output, indent=2)
        os.chmod(path, 0o600)
        Rhino.RhinoApp.WriteLine("VisoLoom local framing: {0}. Report: {1}".format("passed" if report["passed"] else "needs attention", path))


try:
    run()
except StopIteration:
    Rhino.RhinoApp.WriteLine("Run VisoLoom once to load the plug-in, then run this check again.")
