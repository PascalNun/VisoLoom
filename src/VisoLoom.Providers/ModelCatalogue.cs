using System.Runtime.InteropServices;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed record ModelCatalogueEntry(string Id, string Name, string Location, string Strengths,
    string Hardware, string Storage, string Setup, string? EngineProfileId = null);

// Setup choices are deliberately separate from discovered rendering capabilities.
// These descriptions reflect the reviewed adapters; they never imply installation.
// A local choice names the one reviewed engine profile it installs toward, so
// the settings list can pair it with the running engine's live capabilities.
public static class ModelCatalogue
{
    public static IReadOnlyList<ModelCatalogueEntry> Choices { get; } = Array.AsReadOnly(new[]
    {
        new ModelCatalogueEntry("local-klein", "FLUX.2 klein 4B", "This computer",
            "Style and refine your Rhino capture entirely on this computer.",
            "Requires Apple Silicon. The model and Rhino share system memory; larger images and scenes need more memory.",
            "About 16 GB of source files and 4.6 GB for the prepared model. Allow at least 11 GB free for preparation, plus space for the engine.",
            "Requires a prepared model and a running local engine. Finding model files does not prepare or activate them.",
            ManagedModels.KleinFourBit.Id),
        new ModelCatalogueEntry("openai", "OpenAI image editing", "Cloud",
            "Style and refine your captured view with OpenAI's image-editing model.",
            "Internet connection and an OpenAI API key. Billed separately from ChatGPT.",
            "No model download. Completed images are saved on this computer.",
            "Cloud rendering sends the source image, prompt and selected scene guidance to OpenAI."),
        new ModelCatalogueEntry("google", "Google image editing", "Cloud",
            "Style and refine your captured view with Google's Nano Banana image models.",
            "Internet connection and a Google API key. Billed per generated image.",
            "No model download. Completed images are saved on this computer.",
            "Cloud rendering sends the source image, prompt and selected scene guidance to Google."),
        new ModelCatalogueEntry("xai", "xAI image editing", "Cloud",
            "Style and refine your captured view with xAI's Grok Imagine image models.",
            "Internet connection and an xAI API key. Billed per generated image plus the input image.",
            "No model download. Completed images are saved on this computer.",
            "Cloud rendering sends the source image, prompt and selected scene guidance to xAI."),
        new ModelCatalogueEntry("bfl", "BFL FLUX.2 image editing", "Cloud",
            "Choose BFL's documented FLUX.2 pro models; rendering itself stays off until the reference-upload decision is resolved.",
            "Internet connection and a BFL API key. Billed per request in credits.",
            "No model download. Completed images are saved on this computer.",
            "The connection checks the key without an image. BFL takes hosted image URLs and documents no upload surface, so no source image is sent anywhere until that contract is resolved."),
        new ModelCatalogueEntry("local-flux3", "FLUX.3 Image Klein Q4K", "This computer",
            "Edit a Rhino capture with the prepared native local engine.",
            "Requires the Linux x64 engine. Native macOS and Windows engines are not available for this model. CPU rendering can be slow.",
            "About 6.4 GB for the reviewed model files, plus the engine and working space.",
            "Requires a prepared installation. The FLUX Kommunity License applies; review the terms before obtaining model files.",
            ManagedModels.KleinFourKLocal.Id),
        new ModelCatalogueEntry("local-klein-native", "FLUX.2 klein 4B native", "This computer",
            "One-click download of the reviewed FLUX.2 klein 4B package, rendered on this computer by the bundled native runner.",
            "Apple Silicon Mac with Metal today; the Windows NVIDIA CUDA runner is the next platform, then the reviewed x86-64 Linux build. The model and Rhino share memory on Apple Silicon.",
            "About 5.2 GB downloaded to your model folder, plus working space.",
            "Install model… downloads and verifies the pinned files. A reviewed runner for this computer must be staged and pass a real edit before rendering.",
            ManagedModels.KleinFourBNative.Id)
    });

    static ModelCatalogue()
    {
        foreach (var entry in Choices)
        {
            if (entry.EngineProfileId is not { } profile)
                continue;
            if (ManagedModels.Find(profile) is null)
                throw new InvalidOperationException("A model choice names an unreviewed engine profile.");
            if (Choices.Count(other => other.EngineProfileId == profile) > 1)
                throw new InvalidOperationException("Each reviewed engine profile backs one model choice.");
        }
    }

    public static bool SupportsCurrentLocalEngine => OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64;

    public static string ComputerSummary => DescribeComputer(
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : "other",
        RuntimeInformation.OSArchitecture);

    public static string DescribeComputer(string system, Architecture architecture) => (system, architecture) switch
    {
        ("macOS", Architecture.Arm64) => "Check the local connection to see whether this model is ready on your computer.",
        ("macOS", _) => "The current local engine requires Apple Silicon and cannot run on this computer. Cloud rendering is available.",
        ("Windows", _) => "This local engine is unavailable on Windows. Cloud connections can be used.",
        _ => "The current local engine is unavailable on this operating system."
    };
}
