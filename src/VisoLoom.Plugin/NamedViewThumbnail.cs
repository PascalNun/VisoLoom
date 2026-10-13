using Rhino;
using Rhino.Display;
using System.Drawing.Imaging;

namespace VisoLoom.Plugin;

// Navigation aid only. The reviewed capture remains the authoritative model input.
internal static class NamedViewThumbnail
{
    public static byte[] Create(RhinoDoc doc, string name)
    {
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != doc.RuntimeSerialNumber)
            throw new InvalidOperationException("Activate this model in Rhino to preview its named views.");
        int index = doc.NamedViews.FindByName(name);
        if (index < 0)
            throw new InvalidOperationException("This named view is no longer available.");
        var source = SceneBuilder.FindView(doc, null);
        var views = SceneBuilder.ModelViews(doc);
        var before = views.Select(SceneBuilder.ViewSignature).ToArray();
        uint undo = doc.NextUndoRecordSerialNumber;
        bool modified = doc.Modified;
        long revision = SceneBuilder.Revision;
        using var named = doc.NamedViews[index];
        var camera = named.Viewport;
        if (!camera.GetFrustum(out double left, out double right, out double bottom, out double top, out _, out _) ||
            right <= left || top <= bottom)
            throw new InvalidOperationException("This named view does not have a valid frame.");
        double aspect = (right - left) / (top - bottom);
        if (!double.IsFinite(aspect) || aspect <= 0)
            throw new InvalidOperationException("This named view does not have a valid frame.");
        int width = aspect >= 1 ? 480 : Math.Max(1, (int)Math.Round(320 * aspect));
        int height = aspect >= 1 ? Math.Max(1, (int)Math.Round(480 / aspect)) : 320;
        using var viewport = new RhinoViewport(source.ActiveViewport);
        if (!viewport.SetViewProjection(camera, false))
            throw new InvalidOperationException("Rhino could not prepare this saved camera.");
        viewport.DisplayMode = source.ActiveViewport.DisplayMode;
        // Size's setter can resize the parent Rhino window even on a viewport copy.
        // Capture settings carry the output dimensions without touching that setter.
        viewport.ConstructionGridVisible = false;
        viewport.ConstructionAxesVisible = false;
        viewport.WorldAxesVisible = false;
        using var settings = new ViewCaptureSettings(source, new System.Drawing.Size(width, height), 96)
        {
            Document = doc,
            RasterMode = true,
            ViewArea = ViewCaptureSettings.ViewAreaMapping.View,
            DrawGrid = false,
            DrawAxis = false,
            DrawBackground = true
        };
        settings.SetViewport(viewport);
        settings.MaximizePrintableArea();
        using var bitmap = ViewCapture.CaptureToBitmap(settings)
            ?? throw new InvalidOperationException("Rhino could not draw this thumbnail. Show the view in Rhino to inspect it.");
        if (revision != SceneBuilder.Revision || modified != doc.Modified || undo != doc.NextUndoRecordSerialNumber ||
            !before.SequenceEqual(views.Select(SceneBuilder.ViewSignature)))
            throw new InvalidOperationException("The scene or view changed while preparing the thumbnail. Try again.");
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
