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
        new ModelCatalogueEntry("local-flux3", "FLUX.3 Image Klein Q4K", "This computer",
            "Edit a Rhino capture with the prepared native local engine.",
            "Requires the Linux x64 engine. Native macOS and Windows engines are not available for this model. CPU rendering can be slow.",
            "About 6.4 GB for the reviewed model files, plus the engine and working space.",
            "Requires a prepared installation. The FLUX Kommunity License applies; review the terms before obtaining model files.",
            ManagedModels.KleinFourKLocal.Id)
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
