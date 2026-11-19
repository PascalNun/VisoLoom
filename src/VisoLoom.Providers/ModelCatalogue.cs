using System.Runtime.InteropServices;

namespace VisoLoom.Providers;

public sealed record ModelCatalogueEntry(string Id, string Name, string Location, string Strengths,
    string Hardware, string Storage, string Setup);

// Setup choices are deliberately separate from discovered rendering capabilities.
// These descriptions reflect the reviewed adapters; they never imply installation.
public static class ModelCatalogue
{
    public static IReadOnlyList<ModelCatalogueEntry> Choices { get; } = Array.AsReadOnly(new[]
    {
        new ModelCatalogueEntry("local-klein", "FLUX.2 klein 4B", "This computer",
            "Style and refine your Rhino capture entirely on this computer.",
            "Apple Silicon. Tested with 16 GB memory; rendering shares memory with Rhino.",
            "About 16 GB of source files and 4.6 GB for the prepared model. Allow at least 11 GB free for preparation, plus space for the engine.",
            "Requires a prepared model and a running local engine. Finding model files does not prepare or activate them."),
        new ModelCatalogueEntry("openai", "OpenAI image editing", "Cloud",
            "Style and refine your captured view with OpenAI's image-editing model.",
            "Internet connection and an OpenAI API key. Billed separately from ChatGPT.",
            "No model download. Completed images are saved on this computer.",
            "Cloud rendering sends the source image, prompt and selected scene guidance to OpenAI."),
        new ModelCatalogueEntry("local-flux3", "FLUX.3 Image Klein Q4K", "This computer",
            "Edit a Rhino capture with the prepared native local engine.",
            "Linux x64 CPU. Native Mac and Windows runners are not available. The measured 512 × 448 edit took about 98 minutes on basic hardware.",
            "About 6.4 GB for the reviewed model files, plus the engine and working space.",
            "Requires a prepared installation. The FLUX Kommunity License applies; review the terms before obtaining model files.")
    });

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
