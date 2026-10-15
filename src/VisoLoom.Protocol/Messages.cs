using VisoLoom.Core;

namespace VisoLoom.Protocol;

public enum ServerJobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record CapabilitiesResponse
{
    public string ProtocolVersion { get; init; } = "1.0";
    public IReadOnlyList<string> SceneSchemaVersions { get; init; } = ["1.0"];
    public required string CapabilityRevision { get; init; }
    public required IReadOnlyList<ModelCapabilities> Models { get; init; }
    public required long MaximumUploadBytes { get; init; }
    public required long MaximumImageBytes { get; init; }
    public required long MaximumDecodedPixels { get; init; }
    public int MaximumJsonBytes { get; init; } = 2_097_152;
    public int MaximumParts { get; init; } = 10;
    public required int QueueLimit { get; init; }
    public required int RetentionHours { get; init; }
    public IReadOnlyList<string> AuthenticationModes { get; init; } = ["bearer"];
    public string RestartBehavior { get; init; } = "failIncompletePreserveCompleted";
}

public static class ReferenceCapabilities
{
    public const string ModelId = "development-passthrough";
    public const string ModelVersion = "1.0";
    public const string Revision = "reference-1.0";
    public static CapabilitiesResponse Create(long maximumImageBytes = 67_108_864, long maximumDecodedPixels = 33_554_432, int queueLimit = 4, int retentionHours = 24) => new()
    {
        CapabilityRevision = Revision,
        MaximumImageBytes = maximumImageBytes,
        MaximumUploadBytes = checked(maximumImageBytes + 4_194_304),
        MaximumDecodedPixels = maximumDecodedPixels,
        QueueLimit = queueLimit,
        RetentionHours = retentionHours,
        Models = [
            new()
            {
                Id = ModelId,
                Version = ModelVersion,
                DisplayName = "Development passthrough — unchanged reference",
                IsDevelopmentPassthrough = true,
                SupportsPrompt = false,
                SupportsCancellation = true,
                Resolution = new()
                {
                    IsKnown = true,
                    MinEdge = 1,
                    MaxEdge = 16_384,
                    MinPixels = 1,
                    MaxPixels = maximumDecodedPixels
                }
            }
        ]
    };
}

public sealed record JobSubmission
{
    public string ProtocolVersion { get; init; } = "1.0";
    public required Guid ClientRequestId { get; init; }
    public required string ModelId { get; init; }
    public required string ModelVersion { get; init; }
    public required string CapabilityRevision { get; init; }
    public required RenderIntent Intent { get; init; }
    public required ResolutionPlan Plan { get; init; }
    public string SceneSchemaVersion { get; init; } = "1.0";
    public required string SceneDigest { get; init; }
    public PreparedReference? PreparedReference { get; init; }
}

public sealed record PreparedReference(PixelSize Size, long ByteCount, string Sha256);

public sealed record JobResponse
{
    public required Guid Id { get; init; }
    public required ServerJobState State { get; init; }
    public required long Revision { get; init; }
    public bool CancelRequested { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public double? Progress { get; init; }
    public JobResult? Result { get; init; }
    public JobFailure? Failure { get; init; }
    public bool IsTerminal => State is ServerJobState.Completed or ServerJobState.Failed or ServerJobState.Cancelled;
}

public sealed record JobResult
{
    public required Guid SceneId { get; init; }
    public required string SceneDigest { get; init; }
    public required string ModelId { get; init; }
    public required string ModelVersion { get; init; }
    public string AdapterVersion { get; init; } = "1.0";
    public required ResolutionPlan Plan { get; init; }
    public required IReadOnlyList<OutputAsset> Outputs { get; init; }
    public IReadOnlyList<PassKind> ConsumedPasses { get; init; } = [PassKind.Reference];
    public IReadOnlyList<PassKind> OmittedPasses { get; init; } = [];
    public IReadOnlyList<string> ProcessingHistory { get; init; } = ["byteIdenticalReferenceCopy"];
    public bool IsUnchangedReference { get; init; }
    public IReadOnlyList<SceneWarning> Warnings { get; init; } = [];
}

public sealed record OutputAsset
{
    public required string OutputId { get; init; }
    public required string DownloadPath { get; init; }
    public string MimeType { get; init; } = "image/png";
    public required long ByteCount { get; init; }
    public required string Sha256 { get; init; }
    public required PixelSize Size { get; init; }
}

public sealed record JobFailure(string Code, string Message);
public sealed record ProblemResponse
{
    public string Type { get; init; } = "about:blank";
    public required string Title { get; init; }
    public required int Status { get; init; }
    public required string Code { get; init; }
    public required string CorrelationId { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<ValidationIssue>? Errors { get; init; }
}
