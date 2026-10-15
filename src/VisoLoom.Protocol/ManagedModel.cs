using System.Text;
using VisoLoom.Core;

namespace VisoLoom.Protocol;

// Limits of the reviewed first local editing profile. The service supplies the
// exact installed model version; no network request can select a runtime/path.
public static class ManagedModel
{
    public const string Id = "flux2-klein-4b";
    public const int MaximumPromptBytes = 2048;
    public const long MaximumImageBytes = 16_777_216;
    public static ModelCapabilities Create(string version) => new()
    {
        Id = Id, Version = version, DisplayName = "FLUX.2 klein 4B · this computer",
        SupportsPrompt = true, SupportsCancellation = true, SupportsPadding = true, SupportsResampling = true,
        Resolution = new() { IsKnown = true, MinEdge = 128, MaxEdge = 1536, MinPixels = 16384,
            MaxPixels = 1048576, DimensionMultiple = 64, AllowFractionalSampling = true }
    };

    public static RenderIntent Compose(RepresentationPreset preset, string userPrompt, PixelSize output)
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
