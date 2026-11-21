using System.Text;
using VisoLoom.Core;

namespace VisoLoom.Protocol;

// The reviewed local editing models. Each definition is a complete capability
// contract; the service advertises the definition whose prepared engine passed
// its acceptance test, and no request can select a runtime or path.
public sealed record ManagedModelDefinition(
    string Id,
    string DisplayName,
    int MaximumPromptBytes,
    long MaximumImageBytes,
    ResolutionConstraints Resolution)
{
    public ModelCapabilities Create(string version) => new()
    {
        Id = Id, Version = version, DisplayName = DisplayName,
        SupportsPrompt = true, SupportsCancellation = true, SupportsPadding = true, SupportsResampling = true,
        Resolution = Resolution
    };

    public RenderIntent Compose(RepresentationPreset preset, string userPrompt, PixelSize output)
    {
        if (preset.RequiredPasses.Any(p => p != PassKind.Reference))
            throw new ArgumentException("This local model accepts only the captured reference image.");
        string prompt = string.Join("\n\n", new[] { preset.BaseInstruction, PreservationRules.Objective,
            preset.VegetationPolicy, userPrompt }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (Encoding.UTF8.GetByteCount(prompt) > MaximumPromptBytes || prompt.Contains('\0'))
            throw new ArgumentException("The local editing prompt is too long. Shorten it; your words will not be truncated.");
        return new() { PresetId = preset.Id, PresetRevision = preset.Revision, UserPrompt = userPrompt,
            EffectivePrompt = prompt, RequestedOutput = output };
    }
}

// The set the installation receipt may put in service. Adding a definition is
// a review decision: an engine check naming anything else fails closed.
public static class ManagedModels
{
    // The first reviewed local profile: MFLUX 0.22.0, FLUX.2 klein 4B with 4-bit
    // quantisation. Identifiers are wire-compatible with earlier versions.
    public static readonly ManagedModelDefinition KleinFourBit = new(
        "flux2-klein-4b", "FLUX.2 klein 4B · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1048576, DimensionMultiple = 64, AllowFractionalSampling = true });

    // The second reviewed local profile: stable-diffusion.cpp (ggml) running
    // the FLUX.3 Image Klein transformer quantised to Q4_K on the CPU. The
    // durable native frame contract matches the engine's own bound: edges over
    // 64-pixel multiples up to 1536 pixels and complete frames to 1.5 megapixels.
    public static readonly ManagedModelDefinition KleinFourKLocal = new(
        "flux3-image-klein-q4k", "FLUX.3 Image Klein Q4K · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true });

    // The third reviewed local profile: stable-diffusion.cpp running the
    // reviewed one-click FLUX.2 klein 4B package on each platform's pinned
    // native build — Metal on Apple Silicon, CUDA on Windows once accepted,
    // the x86-64 CPU build on Linux. The durable frame contract matches the
    // prepared MFLUX profile; the installed receipt owns acceptance.
    public static readonly ManagedModelDefinition KleinFourBNative = new(
        "flux2-klein-4b-native", "FLUX.2 klein 4B native · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1048576, DimensionMultiple = 64, AllowFractionalSampling = true });

    public static readonly IReadOnlyList<ManagedModelDefinition> All = [KleinFourBit, KleinFourKLocal, KleinFourBNative];

    public static ManagedModelDefinition? Find(string? id) =>
        All.FirstOrDefault(definition => definition.Id == id);
}
