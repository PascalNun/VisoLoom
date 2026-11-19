using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

// Owns the captured pair. The viewer must never fetch a new viewport image or
// borrow bitmaps that a refreshed panel could dispose or replace underneath it.
public sealed class ComparisonSnapshot
{
    private readonly byte[] source, result;
    public Guid SceneId { get; }
    public Guid ResultId { get; }
    public DateTimeOffset CapturedAt { get; }
    public PixelSize Frame { get; }
    public PixelSize ModelCanvas { get; }
    public bool IsUnchangedReference { get; }
    private ComparisonSnapshot(SceneManifest scene, byte[] source, ProviderResult output)
    {
        SceneId = scene.SceneId;
        ResultId = output.Metadata.ResultId;
        CapturedAt = scene.CapturedAt;
        Frame = output.Metadata.Plan.Final;
        ModelCanvas = output.Metadata.Plan.Native;
        IsUnchangedReference = output.Metadata.IsUnchangedReference;
        this.source = source;
        result = output.PngBytes;
    }
    public Stream OpenSource() => new MemoryStream(source, writable: false);
    public Stream OpenResult() => new MemoryStream(result, writable: false);

    public static ComparisonSnapshot Create(SceneManifest scene, byte[] reference, ProviderResult output, IImageProcessor? images = null)
    {
        const int maximumBytes = 67_108_864;
        if (reference.Length > maximumBytes || output.PngBytes.Length > maximumBytes)
            throw new ArgumentException("The comparison images exceed the inspection limit.");
        // Clone before validation so the checked bytes are exactly those displayed.
        byte[] source = reference.ToArray(), result = output.PngBytes.ToArray();
        ResolutionPlanner.ValidateSize(scene.Frame);
        var pass = scene.Passes.SingleOrDefault(p => p.Kind == PassKind.Reference && p.Status == PassStatus.Available);
        var metadata = output.Metadata;
        ResolutionPlanner.ValidateSize(metadata.Plan.Native);
        ResolutionPlanner.ValidateSize(metadata.Plan.Final, ResolutionPlanner.ExportPixelLimit);
        if (metadata.SceneId != scene.SceneId || metadata.ResultId == Guid.Empty || pass is null ||
            metadata.SceneDigest != ContentDigest.Sha256(JsonSerializer.SerializeToUtf8Bytes(scene, WireJson.Options)) ||
            metadata.Plan.Capture != scene.Frame ||
            PngHeader.Read(source) != scene.Frame || PngHeader.ReadExport(result) != metadata.Plan.Final ||
            pass.ByteCount != source.LongLength || pass.Sha256 != ContentDigest.Sha256(source) ||
            metadata.OutputByteCount != result.LongLength || metadata.OutputSha256 != ContentDigest.Sha256(result))
            throw new ArgumentException("The result and source do not belong to the same captured frame. No new capture was made.");
        if (scene.Frame != metadata.Plan.Final)
        {
            if (images is null)
                throw new ArgumentException("The source must be fitted to the recorded export frame before comparison.");
            source = images.NormalizeResult(source, ResolutionPlanner.ReferencePlan(scene.Frame, metadata.Plan.Final));
            if (source.Length > maximumBytes || PngHeader.ReadExport(source) != metadata.Plan.Final)
                throw new ArgumentException("The fitted comparison source has unexpected dimensions.");
        }
        return new(scene, source, output with { PngBytes = result });
    }
}
