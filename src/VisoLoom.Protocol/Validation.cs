using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VisoLoom.Core;

namespace VisoLoom.Protocol;

public static class ContentDigest
{
    public static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Submission(byte[] request, byte[] scene, IReadOnlyDictionary<string, byte[]> assets)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(request);
        Add(scene);
        foreach (var pair in assets.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            Add(Encoding.UTF8.GetBytes(pair.Key));
            Add(SHA256.HashData(pair.Value));
            Add(Encoding.ASCII.GetBytes(pair.Value.LongLength.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        void Add(byte[] value)
        {
            Span<byte> size = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(size, value.LongLength);
            hash.AppendData(size);
            hash.AppendData(value);
        }
    }
}

public static class PngHeader
{
    // This validates bounded metadata only. Display requires the host's proven PNG decoder.
    public static PixelSize Read(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [
            137,
            80,
            78,
            71,
            13,
            10,
            26,
            10
        ];
        if (bytes.Length < 45 || !bytes[..8].SequenceEqual(signature) || BinaryPrimitives.ReadInt32BigEndian(bytes[8..12]) != 13 || !bytes[12..16].SequenceEqual("IHDR"u8) || bytes[24] != 8 || bytes[25] is not (2 or 6) || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1 || !bytes[^8..^4].SequenceEqual("IEND"u8))
            throw new ArgumentException("An 8-bit RGB/RGBA PNG is required.");
        var size = new PixelSize(BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]), BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]));
        ResolutionPlanner.ValidateSize(size);
        return size;
    }
}

public static class SubmissionValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(JobSubmission request, SceneManifest scene, byte[] reference, CapabilitiesResponse capabilities)
    {
        var issues = SceneValidator.Validate(scene).ToList();
        if (issues.Count != 0)
            return issues;
        void Error(string code, string field, string message) => issues.Add(new(code, field, message));
        if (request.ProtocolVersion != "1.0" || request.SceneSchemaVersion != scene.SchemaVersion)
            Error("unsupportedVersion", "protocolVersion", "Unsupported protocol or schema version.");
        if (request.ClientRequestId == Guid.Empty)
            Error("invalidRequest", "clientRequestId", "A request identifier is required.");
        if (request.CapabilityRevision != capabilities.CapabilityRevision)
            Error("capabilityChanged", "capabilityRevision", "Discover capabilities again before submitting.");
        var model = capabilities.Models.SingleOrDefault(m => m.Id == request.ModelId && m.Version == request.ModelVersion);
        bool managed = request.ModelId == ManagedModel.Id && model is not null;
        if (model is null)
            Error("unsupportedModel", "modelId", "The selected model is not available.");
        if (!managed && (request.ModelId != ReferenceCapabilities.ModelId || request.ModelVersion != ReferenceCapabilities.ModelVersion))
            Error("unsupportedModel", "modelId", "The selected model is not supported.");
        if (!managed && (request.PreparedReference is not null || request.Plan is null || !request.Plan.IsExact || request.Plan.Capture != scene.Frame || request.Intent is null || request.Intent.RequestedOutput != scene.Frame || request.Intent.PresetId != ReferenceCapabilities.ModelId || !string.IsNullOrEmpty(request.Intent.UserPrompt) || !string.IsNullOrEmpty(request.Intent.EffectivePrompt)))
            Error("unsupportedIntent", "intent", "Passthrough supports an exact capture only; modes and prompts have no effect.");
        if (managed)
        {
            try
            {
                if (request.Intent is null || request.Plan is null || request.Plan != ResolutionPlanner.Plan(scene.Frame, request.Intent.RequestedOutput, model!.Resolution) ||
                    request.PreparedReference is not { } prepared || prepared.Size != request.Plan.Native || prepared.ByteCount != reference.LongLength || prepared.Sha256 != ContentDigest.Sha256(reference) ||
                    request.Intent.DisclosedContextFields is not { Count: 0 } || string.IsNullOrEmpty(request.Intent.EffectivePrompt) || request.Intent.EffectivePrompt.Contains('\0') ||
                    !request.Intent.EffectivePrompt.Contains(PreservationRules.Objective, StringComparison.Ordinal) ||
                    Encoding.UTF8.GetByteCount(request.Intent.EffectivePrompt) > ManagedModel.MaximumPromptBytes)
                    Error("unsupportedIntent", "intent", "Preview a complete prepared frame and bounded local editing prompt.");
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            { Error("unsupportedIntent", "plan", "The local model frame plan is not supported."); }
        }
        if (!managed && !ResolutionPlanner.Supports(scene.Frame, capabilities.Models[0].Resolution))
            Error("unsupportedResolution", "frame", "The capture exceeds the model's limits.");
        if (scene.Passes.Any(p => p.Status == PassStatus.Available && p.Kind != PassKind.Reference))
            Error("unsupportedPass", "passes", "Only the reference pass may be uploaded.");
        var pass = scene.Passes.FirstOrDefault(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available);
        if (pass is null || pass.FileName != "reference.png" || !managed && (pass.ByteCount != reference.LongLength || pass.Sha256 != ContentDigest.Sha256(reference)))
            Error("invalidAsset", "reference", "The reference does not match its manifest.");
        if (reference.LongLength > capabilities.MaximumImageBytes || managed && reference.LongLength > ManagedModel.MaximumImageBytes)
            Error("assetTooLarge", "reference", "The reference exceeds the image byte limit.");
        try
        {
            if (PngHeader.Read(reference) != (managed ? request.Plan?.Native : scene.Frame))
                Error("invalidAsset", "reference", "PNG dimensions differ from the capture frame.");
        }
        catch (ArgumentException)
        {
            Error("invalidAsset", "reference", "Unsupported or invalid PNG header.");
        }

        return issues;
    }

    public static T Deserialize<T>(byte[] bytes)
    {
        // Duplicate properties would make exact-byte hashing disagree with semantic interpretation.
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        Check(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, WireJson.Options) ?? throw new JsonException("JSON cannot be null.");
        static void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in element.EnumerateObject())
                {
                    if (!names.Add(p.Name))
                        throw new JsonException("Duplicate JSON property.");
                    Check(p.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray())
                    Check(item);
        }
    }
}
