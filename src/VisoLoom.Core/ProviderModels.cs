namespace VisoLoom.Core;

public sealed record ProviderCapabilities
{
    public required string ProviderId { get; init; }
    public required string CapabilityRevision { get; init; }
    public required IReadOnlyList<ModelCapabilities> Models { get; init; }
    public long? MaximumImageBytes { get; init; }
    public long? MaximumDecodedPixels { get; init; }
}

public sealed record ModelCapabilities
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string DisplayName { get; init; }
    public bool IsDevelopmentPassthrough { get; init; }
    public bool SupportsPrompt { get; init; }
    public bool SupportsCancellation { get; init; }
    public bool SupportsPadding { get; init; }
    public bool SupportsResampling { get; init; }
    public IReadOnlyList<PassKind> RequiredPasses { get; init; } = [PassKind.Reference];
    public IReadOnlyList<PassKind> AcceptedPasses { get; init; } = [PassKind.Reference];
    public required ResolutionConstraints Resolution { get; init; }
}

/// <summary>Successful provider-neutral result provenance. A synchronous API need not invent a remote job.</summary>
public sealed record RenderResult
{
    public required Guid ResultId { get; init; }
    public required Guid SceneId { get; init; }
    public required string SceneDigest { get; init; }
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
    public required string ModelVersion { get; init; }
    public required ResolutionPlan Plan { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string OutputSha256 { get; init; }
    public required long OutputByteCount { get; init; }
    public string? NativeOutputSha256 { get; init; }
    public long? NativeOutputByteCount { get; init; }
    public string AdapterVersion { get; init; } = "1.0";
    public string? RemoteJobId { get; init; }
    public bool IsUnchangedReference { get; init; }
    public IReadOnlyList<PassKind> ConsumedPasses { get; init; } = [];
    public IReadOnlyList<PassKind> OmittedPasses { get; init; } = [];
    public IReadOnlyList<string> DisclosedContextFields { get; init; } = [];
    public IReadOnlyList<string> ProcessingHistory { get; init; } = [];
    public IReadOnlyList<SceneWarning> Warnings { get; init; } = [];
}
