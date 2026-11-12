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
            "The current local engine requires Apple Silicon. Tested with 16 GB memory; large Rhino scenes still need memory testing.",
            "About 16 GB of source weights, plus a 4.62 GB prepared copy. Keep 10 GiB free for preparation; the runtime uses additional space.",
            "Experimental local editing. Find an existing model folder, review its setup, then check the connection. Automatic installation is planned."),
        new ModelCatalogueEntry("openai", "OpenAI image editing", "Cloud",
            "Style and refine your captured view with OpenAI's image-editing model.",
            "Requires an internet connection and an OpenAI API key. API billing is separate from ChatGPT.",
            "No model download. Completed images are saved on this computer.",
            "Add your key and allow cloud rendering in Settings. Render sends the capture and selected scene context to OpenAI.")
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
