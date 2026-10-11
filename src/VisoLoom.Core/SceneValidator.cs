namespace VisoLoom.Core;

public static class SceneValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(SceneManifest scene)
    {
        List<ValidationIssue> issues = [];
        void Error(string field, string message, string code = "invalidScene") => issues.Add(new(code, field, message));
        if (!IsCompatibleVersion(scene.SchemaVersion))
            Error("schemaVersion", "Unsupported scene schema version.", "unsupportedVersion");
        if (scene.SceneId == Guid.Empty)
            Error("sceneId", "A scene identifier is required.");
        if (scene.CapturedAt == default || scene.CapturedAt.Offset != TimeSpan.Zero)
            Error("capturedAt", "A UTC capture timestamp is required.");
        if (scene.RequiredFeatures is null || scene.RequiredFeatures.Count != 0)
            Error("requiredFeatures", "This reader does not support additional required features.");
        if (scene.PixelConvention != "topLeftPixelCenters")
            Error("pixelConvention", "Unsupported pixel convention.");
        try
        {
            ResolutionPlanner.ValidateSize(scene.Frame);
        }
        catch (ArgumentException)
        {
            Error("frame", "Invalid capture dimensions.");
        }

        if (scene.Source is null || scene.Snapshot is null || scene.Camera is null || scene.View is null || scene.Camera.Frustum is null)
        {
            Error("scene", "Source, snapshot, camera and view are required.");
            return issues;
        }

        if (!double.IsFinite(scene.Source.ModelTolerance) || scene.Source.ModelTolerance <= 0)
            Error("source.modelTolerance", "Model tolerance must be positive and finite.");
        if (string.IsNullOrWhiteSpace(scene.Source.DocumentSessionId) || scene.Source.DocumentSessionId != scene.Snapshot.DocumentSessionId || scene.Snapshot.ViewportId == Guid.Empty || scene.Snapshot.ViewportId != scene.View.ViewportId || string.IsNullOrWhiteSpace(scene.Snapshot.ConsistencyToken))
            Error("snapshot", "Snapshot identifiers must agree.");
        if (!Enum.IsDefined(scene.Camera.Projection) || !Finite(scene.Camera.Position) || !Direction(scene.Camera.Direction) || !Direction(scene.Camera.Up))
            Error("camera", "Invalid projection or camera vectors.");
        var direction = scene.Camera.Direction;
        var up = scene.Camera.Up;
        if (Math.Abs(direction.X * up.X + direction.Y * up.Y + direction.Z * up.Z) > 1e-6)
            Error("camera.up", "Camera direction and up must be orthogonal.");
        var f = scene.Camera.Frustum;
        if (!new[]
        {
            f.Left,
            f.Right,
            f.Bottom,
            f.Top,
            f.Near,
            f.Far
        }.All(double.IsFinite) || f.Left >= f.Right || f.Bottom >= f.Top || (scene.Camera.Projection != ProjectionKind.Parallel && f.Near <= 0) || f.Far <= f.Near)
            Error("camera.frustum", "A finite ordered camera frustum is required.");
        var viewport = scene.Camera.ViewportPixels;
        if (viewport.Width <= 0 || viewport.Height <= 0 || (long)viewport.Width * scene.Frame.Height != (long)viewport.Height * scene.Frame.Width)
            Error("camera.viewportPixels", "Capture must preserve the viewport aspect ratio.");
        if (scene.Passes is null || scene.Layers is null || scene.Materials is null || scene.Objects is null || scene.Bindings is null || scene.Clipping is null)
        {
            Error("scene", "Scene collections cannot be null.");
            return issues;
        }

        if (scene.Passes.Count > 8 || scene.Passes.Any(p => p is null))
        {
            Error("passes", "Invalid pass list.");
            return issues;
        }

        var files = new HashSet<string>(StringComparer.Ordinal);
        var kinds = new HashSet<PassKind>();
        foreach (var p in scene.Passes)
        {
            if (!Enum.IsDefined(p.Kind) || !Enum.IsDefined(p.Status) || !kinds.Add(p.Kind))
                Error("passes", "Pass kinds must be valid and unique.");
            if (p.Status == PassStatus.Available)
            {
                if (!IsLogicalAssetName(p.FileName) || !files.Add(p.FileName!))
                    Error("passes.fileName", "Asset names must be safe and unique.");
                if (p.MimeType != "image/png" || p.Size != scene.Frame || p.ByteCount is null or <= 0 || p.BitDepth is not (8 or 16) || !IsSha256(p.Sha256))
                    Error("passes", "Available passes need matching dimensions, PNG metadata and digest.");
            }
            else if (p.FileName is not null || p.ByteCount is not null || p.Sha256 is not null)
                Error("passes", "Unavailable passes cannot name an asset.");
        }

        if (!scene.Passes.Any(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available))
            Error("passes", "A reference image is required.", "missingPass");
        var layers = Ids(scene.Layers.Select(x => x?.Id ?? Guid.Empty), "layers");
        var materials = Ids(scene.Materials.Select(x => x?.Id ?? Guid.Empty), "materials");
        var objects = Ids(scene.Objects.Select(x => x?.Id ?? Guid.Empty), "objects");
        Ids(scene.Clipping.Select(x => x?.Id ?? Guid.Empty), "clipping");
        if (scene.Layers.Any(x => x is null || x.ParentId is { } id && !layers.Contains(id)))
            Error("layers", "Layer parent references must resolve.");
        if (scene.Objects.Any(x => x is null || !layers.Contains(x.LayerId)))
            Error("objects", "Object layer references must resolve.");
        if (scene.Bindings.Any(x => x is null || !objects.Contains(x.ObjectId) || x.MaterialId is { } id && !materials.Contains(id)))
            Error("bindings", "Material bindings must resolve.");
        foreach (var m in scene.Materials.Where(x => x is not null))
            if (new[]
            {
            m.Roughness,
            m.Metallic,
            m.Opacity
        }.Any(x => x is { } v && (!double.IsFinite(v) || v < 0 || v > 1)))
                Error("materials", "Appearance values must be finite in [0,1].");
        foreach (var c in scene.Clipping.Where(x => x is not null))
            if (!Direction(c.Normal) || !double.IsFinite(c.D) || c.Depth is { } depth && (!double.IsFinite(depth) || depth < 0))
                Error("clipping", "Clipping values must be finite and valid.");
        return issues;
        HashSet<Guid> Ids(IEnumerable<Guid> ids, string field)
        {
            var result = new HashSet<Guid>();
            foreach (var id in ids)
                if (id == Guid.Empty || !result.Add(id))
                    Error(field, "Identifiers must be nonempty and unique.");
            return result;
        }
    }

    public static bool IsCompatibleVersion(string? version) => version?.Split('.') is ["1", var minor] && minor.Length is > 0 and <= 9 && minor.All(char.IsAsciiDigit);
    public static bool IsLogicalAssetName(string? value) => value is { Length: > 0 and <= 80 } && value.EndsWith(".png", StringComparison.Ordinal) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !value.Contains("..", StringComparison.Ordinal) && !value.StartsWith('.');
    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static bool Finite(Vector3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
    private static bool Direction(Vector3 v) => Finite(v) && Math.Abs(v.X * v.X + v.Y * v.Y + v.Z * v.Z - 1) < 1e-6;
}
