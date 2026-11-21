using System.Runtime.InteropServices;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed record ModelCatalogueEntry(string Id, string Name, string Location, string Strengths,
    string Hardware, string Storage, string Setup, string? EngineProfileId = null, string? DownloadPackageId = null,
    string? Fit = null, string? PublisherUrl = null, string? LicenceUrl = null, string? WeightsNote = null);

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
            "Runs on Apple Silicon with Metal. The model and Rhino share memory.",
            "About 5.2 GB downloaded to your model folder, plus working space.",
            "Install model… downloads and verifies the pinned files. A reviewed runner for this computer must be staged and pass a real edit before rendering.",
            ManagedModels.KleinFourBNative.Id, DownloadPackageId: "flux2-klein-4b-gguf",
            Fit: "Compact · 5.2 GB download"),
        new ModelCatalogueEntry("flux3-image-klein", "FLUX 3 Image Klein", "This computer",
            "Newer FLUX option for whole-view photorealism and illustration with a complete caption.",
            "Compact: the quantised transformer and its Qwen3-VL 4B encoder fit alongside Rhino on a workstation.",
            "6.0 GB verified transformer and encoder; the autoencoder comes from Black Forest Labs' licensed repository.",
            "Get the weights at the publisher, then link the folder with Use existing files….",
            Fit: "Compact · 6.0 GB verified",
            PublisherUrl: "https://huggingface.co/black-forest-labs/flux-3-image-klein",
            LicenceUrl: "https://huggingface.co/black-forest-labs/flux-3-image-klein/blob/main/LICENSE.md",
            WeightsNote: "The FLUX Kommunity licence covers commercial outputs for qualifying users; review its conditions before downloading."),
        new ModelCatalogueEntry("qwen-image-edit-2511", "Qwen-Image-Edit-2511", "This computer",
            "First larger non-FLUX comparison: carries Rhino's assigned materials and architectural detail into whole-view edits.",
            "High-memory workstation: the quantised recipe shares memory with Rhino during rendering.",
            "18.6 GB verified download: transformer 12.8 GB, encoder and vision projector 5.5 GB, VAE 0.3 GB.",
            "Apache-2.0. Install model downloads and verifies the pinned files. Rendering needs this family's runner recipe; the engine reports that honestly until it ships.",
            DownloadPackageId: "qwen-image-edit-2511-gguf",
            Fit: "High-memory workstation · 18.6 GB download"),
        new ModelCatalogueEntry("longcat-image-edit", "LongCat-Image-Edit-Turbo", "This computer",
            "Broad restyling and competition sketches: the quick non-FLUX treatment to try beside klein 4B.",
            "Workstation: the quantised recipe shares memory with Rhino during rendering.",
            "9.4 GB verified transformer, encoder and vision projector; the FLUX.1 autoencoder is a licensed companion download.",
            "Get the weights at the publisher, then link the folder with Use existing files….",
            Fit: "Workstation · 9.4 GB verified",
            PublisherUrl: "https://huggingface.co/meituan-longcat/LongCat-Image-Edit-Turbo",
            LicenceUrl: "https://huggingface.co/black-forest-labs/FLUX.1-dev/blob/main/LICENSE.md",
            WeightsNote: "The model is Apache-2.0; its FLUX.1 autoencoder sits behind Black Forest Labs' licence acceptance."),
        new ModelCatalogueEntry("flux2-klein-9b", "FLUX.2 klein 9B", "This computer",
            "Licensed step up from klein 4B for photorealistic detail; compare it on the same captures before client work.",
            "Workstation: the quantised recipe shares memory with Rhino during rendering.",
            "10.6 GB verified transformer and encoder; the FLUX.2 autoencoder comes from Black Forest Labs' licensed repository.",
            "Get the weights at the publisher, then link the folder with Use existing files….",
            Fit: "Workstation · 10.6 GB verified",
            PublisherUrl: "https://huggingface.co/black-forest-labs/FLUX.2-klein-9B",
            LicenceUrl: "https://huggingface.co/black-forest-labs/FLUX.2-klein-9B/blob/main/LICENSE.md",
            WeightsNote: "Commercial operation requires BFL rights for the model; review the licence terms before downloading."),
        new ModelCatalogueEntry("flux2-dev", "FLUX.2 dev", "This computer",
            "High-memory comparison for detailed materials, lighting and multi-reference work; lower priority than the klein models.",
            "High-memory workstation: the quantised recipe shares memory with Rhino during rendering.",
            "About 33.6 GB quantised: transformer 19.3 GB and encoder 14.3 GB, plus the licensed autoencoder.",
            "Get the weights at the publisher, then link the folder with Use existing files….",
            Fit: "High-memory workstation · about 33.6 GB",
            PublisherUrl: "https://huggingface.co/black-forest-labs/FLUX.2-dev",
            LicenceUrl: "https://bfl.ai/open-weights",
            WeightsNote: "BFL's model terms are non-commercial unless separately licensed; review them before downloading."),
        new ModelCatalogueEntry("llada-image-turbo", "LLaDA-Image-Turbo", "This computer",
            "Alternative sketch and quick-restyling contender; check whether thin geometry survives its treatments.",
            "High-memory workstation until a quantised conversion is reviewed.",
            "About 55 GB from the original repository: transformer, text encoder, tokenizer files and the included VAE.",
            "Get the weights at the publisher, then link the folder with Use existing files….",
            Fit: "High-memory workstation · about 55 GB",
            PublisherUrl: "https://huggingface.co/inclusionAI/LLaDA-Image-Turbo",
            LicenceUrl: "https://huggingface.co/inclusionAI/LLaDA-Image-Turbo",
            WeightsNote: "Apache-2.0. The original repository's full-precision components make this the heaviest local option today."),
        new ModelCatalogueEntry("flux3-image-klein-base", "FLUX 3 Image Klein Base", "This computer",
            "Undistilled variant for fine-tuning or a dedicated LoRA evaluation, with a longer schedule and guidance.",
            "Workstation: start ordinary rendering from the distilled checkpoint.",
            "6.0 GB verified transformer and encoder; the autoencoder comes from Black Forest Labs' licensed repository.",
            "Get the weights at the publisher, then link the folder with Use existing files….",
            Fit: "Workstation · 6.0 GB verified",
            PublisherUrl: "https://huggingface.co/black-forest-labs/flux-3-image-klein-base",
            LicenceUrl: "https://huggingface.co/black-forest-labs/flux-3-image-klein/blob/main/LICENSE.md",
            WeightsNote: "The FLUX Kommunity licence covers commercial outputs for qualifying users; review its conditions before downloading.")
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
