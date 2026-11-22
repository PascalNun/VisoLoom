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
    ResolutionConstraints Resolution,
    // The reviewed recipe's sampling steps and the bounds a per-render
    // override may take — mirrors of the engine profile's frozen values and
    // STEP_BOUNDS (ADR 0022 generation identity; the engine's worker rejects
    // anything outside them, so the two sides never disagree silently).
    int RecipeSteps,
    int MinSteps,
    int MaxSteps)
{
    public ModelCapabilities Create(string version) => new()
    {
        Id = Id, Version = version, DisplayName = DisplayName,
        SupportsPrompt = true, SupportsCancellation = true, SupportsPadding = true, SupportsResampling = true,
        Resolution = Resolution
    };

    public RenderIntent Compose(RepresentationPreset preset, string userPrompt, PixelSize output, int? samplingSteps = null)
    {
        if (preset.RequiredPasses.Any(p => p != PassKind.Reference))
            throw new ArgumentException("This local model accepts only the captured reference image.");
        // An override outside the profile's bounds is refused here, where the
        // user clicked; the engine's worker rejects it again on its own.
        if (samplingSteps is { } steps && (steps < MinSteps || steps > MaxSteps))
            throw new ArgumentException($"Step overrides for this model stay between {MinSteps} and {MaxSteps}.");
        string prompt = string.Join("\n\n", new[] { preset.BaseInstruction, PreservationRules.Objective,
            preset.VegetationPolicy, userPrompt }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (Encoding.UTF8.GetByteCount(prompt) > MaximumPromptBytes || prompt.Contains('\0'))
            throw new ArgumentException("The local editing prompt is too long. Shorten it; your words will not be truncated.");
        return new() { PresetId = preset.Id, PresetRevision = preset.Revision, UserPrompt = userPrompt,
            EffectivePrompt = prompt, RequestedOutput = output, SamplingSteps = samplingSteps };
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
            MaxPixels = 1048576, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 4, MinSteps: 1, MaxSteps: 16);

    // The second reviewed local profile: stable-diffusion.cpp (ggml) running
    // the FLUX.3 Image Klein transformer quantised to Q4_K on the CPU. The
    // durable native frame contract matches the engine's own bound: edges over
    // 64-pixel multiples up to 1536 pixels and complete frames to 1.5 megapixels.
    public static readonly ManagedModelDefinition KleinFourKLocal = new(
        "flux3-image-klein-q4k", "FLUX.3 Image Klein Q4K · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 16, MinSteps: 1, MaxSteps: 32);

    // The third reviewed local profile: stable-diffusion.cpp running the
    // reviewed one-click FLUX.2 klein 4B package on each platform's pinned
    // native build — Metal on Apple Silicon, CUDA on Windows once accepted,
    // the x86-64 CPU build on Linux. The durable frame contract matches the
    // prepared MFLUX profile; the installed receipt owns acceptance.
    public static readonly ManagedModelDefinition KleinFourBNative = new(
        "flux2-klein-4b-native", "FLUX.2 klein 4B native · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1048576, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 4, MinSteps: 1, MaxSteps: 16);


    // The one-click families on the shared pinned native runner, in the
    // installer's own catalogue order. Recipes and bounds mirror the engine
    // profiles exactly; acceptance stays per installation receipt.
    public static readonly ManagedModelDefinition Flux3KleinNative = new(
        "flux3-image-klein-native", "FLUX.3 Image Klein · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 16, MinSteps: 1, MaxSteps: 40);

    public static readonly ManagedModelDefinition Flux3KleinBaseNative = new(
        "flux3-image-klein-base-native", "FLUX.3 Image Klein Base · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 20, MinSteps: 4, MaxSteps: 60);

    public static readonly ManagedModelDefinition Flux2Klein9b = new(
        "flux2-klein-9b-native", "FLUX.2 klein 9B · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 4, MinSteps: 1, MaxSteps: 16);

    public static readonly ManagedModelDefinition QwenImageEdit2511 = new(
        "qwen-image-edit-2511-native", "Qwen-Image-Edit-2511 · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 20, MinSteps: 4, MaxSteps: 60);

    public static readonly ManagedModelDefinition LongcatImageEdit = new(
        "longcat-image-edit-native", "LongCat-Image-Edit-Turbo · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 8, MinSteps: 1, MaxSteps: 24);

    public static readonly ManagedModelDefinition Flux2Dev = new(
        "flux2-dev-native", "FLUX.2 dev · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 20, MinSteps: 4, MaxSteps: 60);

    public static readonly ManagedModelDefinition LladaImageTurbo = new(
        "llada-image-turbo-native", "LLaDA-Image-Turbo · this computer", 2048, 16_777_216,
        new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1572864, DimensionMultiple = 64, AllowFractionalSampling = true },
        RecipeSteps: 4, MinSteps: 1, MaxSteps: 16);

    public static readonly IReadOnlyList<ManagedModelDefinition> All =
    [KleinFourBit, KleinFourKLocal, KleinFourBNative, Flux3KleinNative, Flux3KleinBaseNative,
     Flux2Klein9b, QwenImageEdit2511, LongcatImageEdit, Flux2Dev, LladaImageTurbo];

    public static ManagedModelDefinition? Find(string? id) =>
        All.FirstOrDefault(definition => definition.Id == id);
}
