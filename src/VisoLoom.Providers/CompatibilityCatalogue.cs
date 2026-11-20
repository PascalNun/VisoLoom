using System.Reflection;
using System.Text.Json;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

// Bundled compatibility data for model selection (model-selection plan,
// "Availability and updates"). The catalogue holds model ids, profile
// references, labels and recommendation/retirement data only. Compiled
// adapters own endpoints, credentials, byte and pixel ceilings and request
// behaviour, so a catalogue entry that no compiled profile vouches for fails
// closed here rather than advertising something the plug-in cannot render.

public sealed record CompatibilityCatalogueEntry(
    string ProviderId, string ModelId, string Label, string ProfileRef,
    bool Recommended = false, bool Retired = false, string? SuccessorModelId = null);

public sealed record CompatibilityCatalogue
{
    public int Format { get; init; } = 1;
    public required string Revision { get; init; }
    public required IReadOnlyList<CompatibilityCatalogueEntry> Models { get; init; }

    public CompatibilityCatalogueEntry? Find(string? providerId, string? modelId) =>
        Models.FirstOrDefault(entry => entry.ProviderId == providerId && entry.ModelId == modelId);
}

public static class CompatibilityCatalogueStore
{
    public const string ResourceName = "VisoLoom.Providers.ModelCompatibilityCatalogue.json";
    private const int MaximumEntries = 64;

    // Null on any invalid bundled copy: a drifted catalogue advertises nothing,
    // and the plug-in keeps rendering with its compiled profiles alone.
    // Recommendation, retirement wording and onboarding then simply have
    // nothing secure to say; tests fail loudly on the shipped file instead.
    public static CompatibilityCatalogue? Current => cached.Value;
    private static readonly Lazy<CompatibilityCatalogue?> cached =
        new(() => TryLoad(typeof(CompatibilityCatalogueStore).Assembly), LazyThreadSafetyMode.PublicationOnly);

    public static CompatibilityCatalogue? TryLoad(Assembly assembly)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return null;
            using var data = new MemoryStream();
            stream.CopyTo(data);
            return Parse(data.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or IOException)
        {
            return null;
        }
    }

    public static CompatibilityCatalogue Parse(byte[] data)
    {
        var catalogue = SubmissionValidator.Deserialize<CompatibilityCatalogue>(data);
        if (catalogue.Revision is not { Length: > 0 and <= 32 } revision ||
            !revision.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            throw new ArgumentException("The compatibility catalogue revision is invalid.");
        if (catalogue.Models is not { Count: > 0 and <= MaximumEntries })
            throw new ArgumentException("The compatibility catalogue must list at least one supported model.");
        var identified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalogue.Models)
        {
            if (entry.ProviderId is not ("openai" or "local"))
                throw new ArgumentException($"The catalogue names provider '{entry.ProviderId}', which no shipped adapter implements.");
            if (!IsIdentifier(entry.ModelId) || !IsIdentifier(entry.ProfileRef))
                throw new ArgumentException("A catalogue model or profile reference is not a bounded identifier.");
            if (entry.Label is not { Length: > 0 and <= 80 } label || label.Any(char.IsControl))
                throw new ArgumentException("A catalogue label is missing or contains control characters.");
            if (entry.SuccessorModelId is { } successor && !IsIdentifier(successor))
                throw new ArgumentException("A catalogue successor identifier is invalid.");
            if (!identified.Add(entry.ProviderId + "\\" + entry.ModelId))
                throw new ArgumentException("The compatibility catalogue lists a model twice.");
            // Retired entries are history, not render capability: their ids may name
            // exactly the snapshot or local profile the compiled adapter has moved past.
            if (entry.ProviderId == "openai" && !entry.Retired && (entry.ModelId != OpenAiImageProvider.ModelId || entry.ProfileRef != "openai-edits-2026-10-11"))
                throw new ArgumentException("The catalogue names an OpenAI profile the pinned adapter does not review.");
            if (entry.ProviderId == "local" && !entry.Retired && ManagedModels.Find(entry.ModelId) is null)
                throw new ArgumentException("The catalogue names a local model without a reviewed engine profile.");
            if (entry.Retired && entry.Recommended)
                throw new ArgumentException("A retired catalogue model cannot stay recommended.");
        }
        foreach (var entry in catalogue.Models)
        {
            if (entry.SuccessorModelId is { } successor && catalogue.Find(entry.ProviderId, successor) is not { Retired: false })
                throw new ArgumentException("Retirement data must name a current replacement model in the catalogue.");
        }
        foreach (var provider in catalogue.Models.Select(entry => entry.ProviderId).Distinct(StringComparer.Ordinal))
            if (catalogue.Models.Count(entry => entry.ProviderId == provider && entry.Recommended) > 1)
                throw new ArgumentException($"Provider '{provider}' has more than one recommended model.");
        return catalogue;
    }

    private static bool IsIdentifier(string? value) => value is { Length: > 0 and < 128 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' or ':' or '+' or '@');
}

// "Recommended uses the tested model for the selected provider. Show a
// recommendation change before the next preview." A local model is tested per
// computer and engine profile, so local entries carry no catalogue
// recommendation; the running engine's own acceptance remains that signal.
public static class RecommendationPolicy
{
    public static CompatibilityCatalogueEntry? Recommended(CompatibilityCatalogue? catalogue, string providerId) =>
        catalogue?.Models.FirstOrDefault(entry => entry.ProviderId == providerId && entry.Recommended && !entry.Retired);

    // Nothing to show on a first session or while the session already renders
    // the recommended model; the notice is guidance for the next preview.
    public static string? ChangeNotice(CompatibilityCatalogue? catalogue, string providerId,
        string? lastSeenModelId, string? selectedModelId)
    {
        var recommended = Recommended(catalogue, providerId);
        if (recommended is null || lastSeenModelId is null || selectedModelId is null ||
            lastSeenModelId == recommended.ModelId || selectedModelId == recommended.ModelId)
            return null;
        string provider = recommended.ProviderId == "openai" ? "OpenAI" : recommended.ProviderId;
        return $"{recommended.Label} is now the recommended {provider} model. Review the Model menu before your next preview.";
    }
}
