namespace VisoLoom.Core;

public readonly record struct PixelSize(int Width, int Height)
{
    public long Pixels => (long)Width * Height;
}

public readonly record struct Vector3(double X, double Y, double Z);
public sealed record Frustum(double Left, double Right, double Bottom, double Top, double Near, double Far);
public enum ProjectionKind { Parallel, Perspective, TwoPointPerspective }
public enum PassKind { Reference, Edges, Depth, Normals, MaterialId, SemanticId, ObjectId, SectionMask }
public enum PassStatus { Available, NotRequested, Unsupported, Failed }

/// <summary>A detached snapshot. Collections and asset bytes must not be changed after publication.</summary>
public sealed record SceneManifest
{
    public string SchemaVersion { get; init; } = "1.0";
    public required Guid SceneId { get; init; }
    public required DateTimeOffset CapturedAt { get; init; }
    public required SceneSource Source { get; init; }
    public required SceneSnapshot Snapshot { get; init; }
    public required CameraMetadata Camera { get; init; }
    public required PixelSize Frame { get; init; }
    public string PixelConvention { get; init; } = "topLeftPixelCenters";
    public required ViewMetadata View { get; init; }
    public required IReadOnlyList<ScenePass> Passes { get; init; }
    public IReadOnlyList<LayerMetadata> Layers { get; init; } = [];
    public IReadOnlyList<MaterialMetadata> Materials { get; init; } = [];
    public IReadOnlyList<ObjectMetadata> Objects { get; init; } = [];
    public IReadOnlyList<MaterialBinding> Bindings { get; init; } = [];
    public IReadOnlyList<ClippingMetadata> Clipping { get; init; } = [];
    public IReadOnlyList<string> RequiredFeatures { get; init; } = [];
    public IReadOnlyList<SceneWarning> Warnings { get; init; } = [];
}

public sealed record SceneSource
{
    public string Application { get; init; } = "Rhino";
    public required string ApplicationVersion { get; init; }
    public string ExporterVersion { get; init; } = "0.1.0";
    public required string DocumentSessionId { get; init; }
    public required string Units { get; init; }
    public required double ModelTolerance { get; init; }
}

public sealed record SceneSnapshot
{
    public required string ConsistencyToken { get; init; }
    public required string DocumentSessionId { get; init; }
    public required Guid ViewportId { get; init; }
}

public sealed record CameraMetadata
{
    public required Vector3 Position { get; init; }
    public required Vector3 Direction { get; init; }
    public required Vector3 Up { get; init; }
    public required ProjectionKind Projection { get; init; }
    public required Frustum Frustum { get; init; }
    public required PixelSize ViewportPixels { get; init; }
    public string CoordinateConvention { get; init; } = "rhinoWorldCameraVectors";
}

public sealed record ViewMetadata
{
    public required Guid ViewportId { get; init; }
    public required Guid DisplayModeId { get; init; }
    public Guid? NamedViewId { get; init; }
    public bool? NamedViewModified { get; init; }
    public string AdornmentPolicy { get; init; } = "currentDisplay";
}

public sealed record ScenePass
{
    public required PassKind Kind { get; init; }
    public required PassStatus Status { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public PixelSize? Size { get; init; }
    public int? BitDepth { get; init; }
    public string? Encoding { get; init; }
    public string? Sha256 { get; init; }
    public long? ByteCount { get; init; }
    public string GeneratorVersion { get; init; } = "0.1.0";
    public string? Reason { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed record LayerMetadata
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public Guid? ParentId { get; init; }
    public bool? Visible { get; init; }
}

public sealed record MaterialMetadata
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string DescriptionSource { get; init; } = "unknown";
    public string? BaseColorSrgb { get; init; }
    public double? Roughness { get; init; }
    public double? Metallic { get; init; }
    public double? Opacity { get; init; }
}

public sealed record ObjectMetadata
{
    public required Guid Id { get; init; }
    public required Guid LayerId { get; init; }
    public required string ObjectType { get; init; }
    public string Completeness { get; init; } = "eligibleObjectOnly";
    public IReadOnlyList<Guid> InstancePath { get; init; } = [];
}

public sealed record MaterialBinding
{
    public required Guid ObjectId { get; init; }
    public Guid? MaterialId { get; init; }
    public string? ComponentPath { get; init; }
    public string Source { get; init; } = "unknown";
}

public sealed record ClippingMetadata
{
    public required Guid Id { get; init; }
    public required Vector3 Normal { get; init; }
    public required double D { get; init; }
    public required bool Enabled { get; init; }
    public bool? AppliesToView { get; init; }
    public bool? DepthEnabled { get; init; }
    public double? Depth { get; init; }
    public bool? IsExclusion { get; init; }
    public IReadOnlyList<Guid> ObjectIds { get; init; } = [];
    public IReadOnlyList<Guid> LayerIds { get; init; } = [];
    public string Completeness { get; init; } = "unknown";
}

public sealed record SceneWarning(string Code, string Message);
public sealed record ValidationIssue(string Code, string Field, string Message);
