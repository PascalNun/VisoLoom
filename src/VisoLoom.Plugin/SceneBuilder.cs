using System.Drawing.Imaging;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using VisoLoom.Core;
using VisoLoom.Protocol;
using Vector3 = VisoLoom.Core.Vector3;

namespace VisoLoom.Plugin;

internal sealed record CapturedScene(SceneManifest Manifest, byte[] Png, string ViewName);
internal static class SceneBuilder
{
    private static long revision;
    static SceneBuilder()
    {
        // Capture may re-enter host code. Any observed model/table/selection edit invalidates it.
        RhinoDoc.AddRhinoObject += Changed;
        RhinoDoc.DeleteRhinoObject += Changed;
        RhinoDoc.ReplaceRhinoObject += Changed;
        RhinoDoc.ModifyObjectAttributes += Changed;
        RhinoDoc.LayerTableEvent += Changed;
        RhinoDoc.MaterialTableEvent += Changed;
        RhinoDoc.RenderMaterialsTableEvent += Changed;
        RhinoDoc.InstanceDefinitionTableEvent += Changed;
        RhinoDoc.DocumentPropertiesChanged += Changed;
        RhinoDoc.SelectObjects += Changed;
        RhinoDoc.DeselectObjects += Changed;
        RhinoDoc.CloseDocument += Changed;
    }

    private static void Changed(object? sender, EventArgs e) => Interlocked.Increment(ref revision);
    public static RhinoView[] ModelViews(RhinoDoc doc) => doc.Views.GetViewList(ViewTypeFilter.Model);

    public static RhinoView FindView(RhinoDoc doc, Guid? viewportId)
    {
        var view = viewportId is { } id
            ? ModelViews(doc).SingleOrDefault(v => v.MainViewport.Id == id)
            : doc.Views.ActiveView;
        if (view is null)
            throw new InvalidOperationException("The source viewport is unavailable. Choose an open Rhino model viewport.");
        RequireSupportedView(view);
        return view;
    }

    public static string ViewSignature(RhinoView view) => System.Text.Json.JsonSerializer.Serialize(new
    {
        View = view.RuntimeSerialNumber,
        Camera = Camera(view.ActiveViewport),
        Mode = view.ActiveViewport.DisplayMode.Id
    }, WireJson.Options);

    public static string CameraSignature(RhinoView view) => System.Text.Json.JsonSerializer.Serialize(Camera(view.ActiveViewport), WireJson.Options);

    public static PixelSize ViewportSize(RhinoView view)
    {
        return new(view.ActiveViewport.Size.Width, view.ActiveViewport.Size.Height);
    }

    private static void RequireSupportedView(RhinoView view)
    {
        if (view is RhinoPageView)
            throw new InvalidOperationException("Layout and detail captures are not supported yet. Activate a model viewport.");
        if (view.ActiveViewport.IsTwoPointPerspectiveProjection)
            throw new InvalidOperationException("Two-point perspective needs host validation before it can be captured by VisoLoom.");
        if (view.RealtimeDisplayMode is not null || view.ActiveViewport.DisplayMode.Id == DisplayModeDescription.RaytracedId)
            throw new InvalidOperationException("Raytraced and other live render engines need completion checks before capture. Choose a regular Rhino display style.");
    }

    public static CapturedScene Capture(RhinoDoc doc, Guid expectedViewport, PixelSize size, string sessionId)
    {
        var view = FindView(doc, expectedViewport);
        var viewport = view.ActiveViewport;
        var mode = viewport.DisplayMode;
        ResolutionPlanner.ValidateSize(size);
        long before = Interlocked.Read(ref revision);
        string viewBefore = ViewSignature(view);
        var camera = Camera(viewport);
        if ((long)camera.ViewportPixels.Width * size.Height != (long)camera.ViewportPixels.Height * size.Width)
            throw new InvalidOperationException("Resolution must preserve the Rhino viewport's exact aspect ratio.");
        var warnings = new List<SceneWarning>
        {
            new("eligibleMetadata", "Material and layer metadata describes eligible objects, not proven pixel visibility or occlusion."),
            new("adornments", "Grid and axes are suppressed. Selection highlights and other display adornments may remain; the document is never changed to hide them."),
            new("materialApproximation", "Material names and legacy diffuse colors are read without baking textures. Complex render-content, nested-instance and subobject appearances remain unresolved.")
        };
        if (mode.EnglishName is not ("Wireframe" or "Shaded" or "Rendered"))
            warnings.Add(new("displayStyleValidation", "This native display style has not completed the full texture, transparency and lighting acceptance matrix. Compare its captured appearance with Rhino."));
        var layers = new Dictionary<Guid, LayerMetadata>();
        var materials = new Dictionary<Guid, MaterialMetadata>();
        var objects = new List<ObjectMetadata>();
        var bindings = new List<MaterialBinding>();
        var clips = new List<ClippingMetadata>();
        var settings = new ObjectEnumeratorSettings
        {
            NormalObjects = true,
            LockedObjects = true,
            HiddenObjects = true,
            IncludeLights = false,
            IncludeGrips = false,
            ReferenceObjects = true
        };
        foreach (var obj in doc.Objects.GetObjectList(settings))
        {
            if (objects.Count >= 20_000)
                throw new InvalidOperationException("This capture exceeds the initial 20,000-object metadata limit.");
            var layer = doc.Layers[obj.Attributes.LayerIndex];
            if (obj is ClippingPlaneObject clipping)
            {
                var geometry = clipping.ClippingPlaneGeometry;
                var plane = geometry.Plane;
                geometry.GetClipParticipation(out var objectIds, out var layerIndices, out var exclusion);
                var clipLayerIds = new List<Guid>();
                foreach (int index in layerIndices ?? [])
                {
                    var clipLayer = doc.Layers[index];
                    if (clipLayer is not null)
                    {
                        AddLayer(clipLayer);
                        clipLayerIds.Add(clipLayer.Id);
                    }
                }

                clips.Add(new()
                {
                    Id = obj.Id,
                    Normal = Vec(plane.Normal),
                    D = -(plane.Normal.X * plane.OriginX + plane.Normal.Y * plane.OriginY + plane.Normal.Z * plane.OriginZ),
                    Enabled = geometry.ViewportIds().Length > 0,
                    AppliesToView = geometry.ViewportIds().Contains(viewport.Id),
                    DepthEnabled = geometry.PlaneDepthEnabled,
                    Depth = geometry.PlaneDepthEnabled ? geometry.PlaneDepth : null,
                    IsExclusion = geometry.ParticipationListsEnabled ? exclusion : null,
                    ObjectIds = geometry.ParticipationListsEnabled ? (objectIds ?? []).ToArray() : [],
                    LayerIds = clipLayerIds,
                    Completeness = "viewAndParticipation"
                });
            }

            // Hidden clipping widgets still affect their views. Extract their metadata before visibility filtering.
            if (!layer.IsVisible || !layer.PerViewportIsVisible(viewport.Id) || obj.IsHidden || obj.Attributes.Space != ActiveSpace.ModelSpace)
                continue;
            AddLayer(layer);
            bool instance = obj is InstanceObject;
            objects.Add(new()
            {
                Id = obj.Id,
                LayerId = layer.Id,
                ObjectType = obj.ObjectType.ToString(),
                Completeness = instance ? "instanceContentsUnresolved" : "eligibleObjectOnly"
            });
            if (instance)
            {
                bindings.Add(new()
                {
                    ObjectId = obj.Id,
                    Source = "instanceUnresolved"
                });
                continue;
            }

            var material = obj.GetMaterial(true);
            Guid materialId = material.Id;
            if (materialId == Guid.Empty)
                materialId = new Guid("bb3f7e25-76ad-4ea2-994a-15372ba6f811"); // Stable synthetic Rhino default, not a reused table index.
            materials.TryAdd(materialId, new()
            {
                Id = materialId,
                Name = string.IsNullOrEmpty(material.Name) ? "Rhino default" : material.Name,
                BaseColorSrgb = $"#{material.DiffuseColor.R:X2}{material.DiffuseColor.G:X2}{material.DiffuseColor.B:X2}",
                DescriptionSource = "rhinoMaterialName",
                Opacity = 1 - material.Transparency
            });
            bindings.Add(new()
            {
                ObjectId = obj.Id,
                MaterialId = materialId,
                Source = obj.Attributes.MaterialSource.ToString()
            });
        }

        var capture = new ViewCapture
        {
            Width = size.Width,
            Height = size.Height,
            DrawAxes = false,
            DrawGrid = false,
            DrawGridAxes = false,
            ScaleScreenItems = false,
            TransparentBackground = false
        };
        using var bitmap = capture.CaptureToBitmap(view) ?? throw new InvalidOperationException("Rhino could not capture this viewport.");
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, ImageFormat.Png);
        byte[] png = encoded.ToArray();
        if (before != Interlocked.Read(ref revision) || ViewSignature(FindView(doc, expectedViewport)) != viewBefore)
            throw new InvalidOperationException("The Rhino scene or view changed during capture. Please test capture again.");
        if (PngHeader.Read(png) != size)
            throw new InvalidOperationException("Rhino returned unexpected capture dimensions.");
        var manifest = new SceneManifest
        {
            SceneId = Guid.NewGuid(),
            CapturedAt = DateTimeOffset.UtcNow,
            Source = new()
            {
                ApplicationVersion = RhinoApp.Version.ToString(),
                DocumentSessionId = sessionId,
                Units = doc.ModelUnitSystem.ToString(),
                ModelTolerance = doc.ModelAbsoluteTolerance
            },
            Snapshot = new()
            {
                ConsistencyToken = before.ToString(System.Globalization.CultureInfo.InvariantCulture),
                DocumentSessionId = sessionId,
                ViewportId = viewport.Id
            },
            Camera = camera,
            Frame = size,
            View = new()
            {
                ViewportId = viewport.Id,
                DisplayModeId = mode.Id,
                AdornmentPolicy = "noGridOrAxesOtherAdornmentsMayRemain"
            },
            Passes = Enum.GetValues<PassKind>().Select(kind => kind == PassKind.Reference ? new ScenePass
            {
                Kind = kind,
                Status = PassStatus.Available,
                FileName = "reference.png",
                MimeType = "image/png",
                Size = size,
                BitDepth = 8,
                Encoding = "sRGB",
                Sha256 = ContentDigest.Sha256(png),
                ByteCount = png.LongLength
            } : new ScenePass
            {
                Kind = kind,
                Status = PassStatus.Unsupported,
                Reason = "Auxiliary passes are not implemented."
            }).ToArray(),
            Layers = layers.Values.ToArray(),
            Materials = materials.Values.ToArray(),
            Objects = objects,
            Bindings = bindings,
            Clipping = clips,
            Warnings = warnings
        };
        var issues = SceneValidator.Validate(manifest);
        if (issues.Count != 0)
            throw new InvalidOperationException(issues[0].Message);
        return new(manifest, png, viewport.Name);
        void AddLayer(Layer layer)
        {
            if (layers.ContainsKey(layer.Id))
                return;
            layers.Add(layer.Id, new()
            {
                Id = layer.Id,
                Name = layer.Name,
                ParentId = layer.ParentLayerId == Guid.Empty ? null : layer.ParentLayerId,
                Visible = layer.IsVisible
            });
            if (layer.ParentLayerId != Guid.Empty)
            {
                var parent = doc.Layers.FindId(layer.ParentLayerId);
                if (parent is not null)
                    AddLayer(parent);
            }
        }
    }

    private static CameraMetadata Camera(RhinoViewport viewport)
    {
        if (!viewport.GetFrustum(out double left, out double right, out double bottom, out double top, out double near, out double far))
            throw new InvalidOperationException("Rhino did not supply a valid camera frustum.");
        var direction = viewport.CameraDirection;
        direction.Unitize();
        // CameraUp can be a non-orthogonal world-up hint (including Rhino's templates).
        // CameraY is Rhino's effective screen-up basis; reading it never changes the view.
        var up = viewport.CameraY;
        up.Unitize();
        return new()
        {
            Position = new(viewport.CameraLocation.X, viewport.CameraLocation.Y, viewport.CameraLocation.Z),
            Direction = Vec(direction),
            Up = Vec(up),
            Projection = viewport.IsParallelProjection ? ProjectionKind.Parallel : ProjectionKind.Perspective,
            Frustum = new(left, right, bottom, top, near, far),
            ViewportPixels = new(viewport.Size.Width, viewport.Size.Height)
        };
    }

    private static Vector3 Vec(Rhino.Geometry.Vector3d vector) => new(vector.X, vector.Y, vector.Z);
}
