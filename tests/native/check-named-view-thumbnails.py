# RunPythonScript with the latest build loaded and an authored model active.
# Reads two saved cameras and writes local thumbnails; no restoration or provider calls.
import json
import os
import struct
import tempfile
import Rhino
from System import AppDomain, Array, Object
from System.Reflection import BindingFlags

ASSEMBLY = "VisoLoom"
FLAGS = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic


def run():
    doc = Rhino.RhinoDoc.ActiveDoc
    if doc is None or doc.NamedViews.Count < 2:
        raise Exception("Use an authored model with at least two named views for this check.")
    assembly = next(a for a in AppDomain.CurrentDomain.GetAssemblies() if a.GetName().Name == ASSEMBLY)
    builder = assembly.GetType("VisoLoom.Plugin.SceneBuilder")
    thumbnail = assembly.GetType("VisoLoom.Plugin.NamedViewThumbnail")
    if thumbnail is None:
        raise Exception("Restart Rhino with the latest VisoLoom build first.")
    named = assembly.GetType("VisoLoom.Plugin.NamedViewSource")

    def state():
        return {"objects": int(doc.Objects.Count), "layers": int(doc.Layers.Count),
                "materials": int(doc.Materials.Count), "modified": bool(doc.Modified),
                "undo": int(doc.CurrentUndoRecordSerialNumber), "nextUndo": int(doc.NextUndoRecordSerialNumber),
                "selection": sorted(str(o.Id) for o in doc.Objects.GetSelectedObjects(False, False)),
                "activeView": str(doc.Views.ActiveView.ActiveViewportID),
                "views": [str(builder.GetMethod("ViewSignature", FLAGS).Invoke(None, Array[Object]([v])))
                          for v in builder.GetMethod("ModelViews", FLAGS).Invoke(None, Array[Object]([doc]))],
                "namedViews": [(v.Name, str(named.GetMethod("Definition", FLAGS).Invoke(None, Array[Object]([v]))))
                               for v in doc.NamedViews]}

    before = state()
    directory = tempfile.mkdtemp(prefix="visoloom-named-view-check-")
    report = {"rhino": str(Rhino.RhinoApp.Version), "before": before, "thumbnails": []}
    try:
        for index in (0, 1):
            png = bytearray(thumbnail.GetMethod("Create", FLAGS).Invoke(None, Array[Object]([doc, doc.NamedViews[index].Name])))
            assert bytes(png[:8]) == b"\x89PNG\r\n\x1a\n"
            width, height = struct.unpack(">II", bytes(png[16:24]))
            assert 0 < width <= 480 and 0 < height <= 480
            with open(os.path.join(directory, "thumbnail-" + str(index) + ".png"), "wb") as output:
                output.write(png)
            assert state() == before, "Thumbnail changed document or camera state."
            report["thumbnails"].append({"index": index, "width": width, "height": height, "stateUnchanged": True})
    finally:
        report["after"] = state()
        report["stateUnchanged"] = before == report["after"]
        with open(os.path.join(directory, "report.json"), "w") as output:
            json.dump(report, output, indent=2)
    assert report["stateUnchanged"]
    Rhino.RhinoApp.WriteLine("VisoLoom named-view thumbnail checks passed. Inspect both images locally: " + directory)


run()
