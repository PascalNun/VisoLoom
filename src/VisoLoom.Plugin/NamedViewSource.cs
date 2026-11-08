using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

// Selection is read-only. Only the explicit Show button restores a view in Rhino.
internal sealed class NamedViewSource
{
    public string? Name { get; private set; }
    private Guid? viewportId;
    private CameraMetadata? appliedCamera;
    private string? definitionSignature;

    public void Select(string? name)
    {
        if (Name == name)
            return;
        Name = name;
        viewportId = null;
        appliedCamera = null;
        definitionSignature = null;
    }

    public void Show(RhinoDoc doc)
    {
        int index = FindIndex(doc);
        string definition = Definition(doc.NamedViews[index]);
        var view = SceneBuilder.FindView(doc, null);
        if (!doc.NamedViews.Restore(index, view.MainViewport))
            throw new InvalidOperationException("Rhino could not restore this named view.");
        view.Redraw();
        // The visible viewport frame remains authoritative, including its current aspect.
        viewportId = view.MainViewport.Id;
        definitionSignature = definition;
        appliedCamera = SceneBuilder.ViewCamera(view);
    }

    public RhinoView Resolve(RhinoDoc doc)
    {
        int index = FindIndex(doc);
        if (viewportId is null)
            throw new InvalidOperationException("Click Show in Rhino to restore this named view before previewing it.");
        var view = SceneBuilder.FindView(doc, viewportId);
        if (Definition(doc.NamedViews[index]) != definitionSignature || appliedCamera is null ||
            !CameraComparison.SameView(SceneBuilder.ViewCamera(view), appliedCamera))
            throw new InvalidOperationException("The view has changed. Click Show in Rhino to restore the named view, or choose Current view to use the visible frame.");
        return view;
    }

    private int FindIndex(RhinoDoc doc)
    {
        int index = Name is null ? -1 : doc.NamedViews.FindByName(Name);
        if (index < 0)
            throw new InvalidOperationException("This named view is unavailable. Choose another named view or Current view.");
        return index;
    }

    private static string Definition(ViewInfo view)
    {
        var camera = view.Viewport;
        if (!camera.GetFrustum(out double left, out double right, out double bottom, out double top, out double near, out double far))
            throw new InvalidOperationException("This named view does not have a valid camera frustum.");
        var p = camera.CameraLocation;
        var d = camera.CameraDirection;
        var u = camera.CameraUp;
        return JsonSerializer.Serialize(new
        {
            Position = new[] { p.X, p.Y, p.Z },
            Direction = new[] { d.X, d.Y, d.Z },
            Up = new[] { u.X, u.Y, u.Z },
            Frustum = new[] { left, right, bottom, top, near, far },
            Parallel = camera.IsParallelProjection,
            TwoPoint = camera.IsTwoPointPerspectiveProjection
        }, WireJson.Options);
    }
}
