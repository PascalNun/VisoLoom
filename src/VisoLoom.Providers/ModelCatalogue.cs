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
        new ModelCatalogueEntry("local-klein", "FLUX.2 klein 4B", "This computer · experimental",
            "Style and refine your Rhino capture entirely on this computer.",
            "Apple Silicon Mac. Tested on M1 Pro / 16 GB; memory pressure alongside large Rhino files still needs testing.",
            "About 16 GB of source weights, plus a 4.62 GB prepared copy. Keep 10 GiB free for preparation; the runtime uses additional space.",
            "Use a complete existing model folder. Draw Things checkpoints need a separate connection. Automatic installation is planned."),
        new ModelCatalogueEntry("openai", "OpenAI image editing", "Cloud · OpenAI",
            "Style and refine your captured view with OpenAI's image-editing model.",
            "Internet + API key. API billing is separate from ChatGPT. No local GPU is required.",
            "No model download. Completed images are saved on this computer.",
            "Add your key and allow cloud rendering in Settings. Render sends the capture and selected scene context to OpenAI.")
    });

    public static string ComputerSummary => DescribeComputer(
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : "other",
        RuntimeInformation.OSArchitecture);

    public static string DescribeComputer(string system, Architecture architecture) => (system, architecture) switch
    {
        ("macOS", Architecture.Arm64) => "This Mac has Apple Silicon. The experimental local adapter can be checked; this alone does not establish memory or model readiness.",
        ("macOS", _) => "This Mac does not report Apple Silicon. The current local adapter cannot run here. OpenAI remains an available setup path.",
        ("Windows", _) => "Windows: the local inference adapter is still planned. Existing folders can be inspected, but are not ready for local rendering. OpenAI is available.",
        _ => "This operating system has no implemented local inference adapter."
    };
}
