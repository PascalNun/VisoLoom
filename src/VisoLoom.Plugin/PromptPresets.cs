using System.Text;
using System.Text.Json;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Providers;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

// The choices the Preset dropdown offers: the seven supplied presets embedded
// with the plug-in, plus this user's personal presets from application data.
// The representation default is a per-user preference: the last chosen preset
// id persists in Rhino settings and is restored when its id still exists
// ("last compatible choice"); a deleted or missing id falls back to the
// first supplied preset. Rhino owns nothing here — the native check loads
// this class without a plug-in instance, so settings access stays null-safe.
internal static class PromptPresets
{
    private const string DefaultPresetKey = "Representation.DefaultPresetId";
    private static readonly PersonalPresetStore Store = new();

    public static IReadOnlyList<RepresentationPreset> All { get; private set; } = [];
    public static string? Problem { get; private set; }

    static PromptPresets() => Refresh();

    public static void Refresh()
    {
        var personal = Store.Load();
        All = [.. Supplied(), .. personal.Presets];
        Problem = personal.Problem;
    }

    public static bool IsPersonal(RepresentationPreset preset) =>
        preset.Id.StartsWith("personal-", StringComparison.Ordinal);

    public static RepresentationPreset DefaultChoice()
    {
        string persisted = Settings?.GetString(DefaultPresetKey, "") ?? "";
        return All.FirstOrDefault(choice => choice.Id == persisted) ?? All[0];
    }

    public static void RememberDefault(RepresentationPreset preset) => Settings?.SetString(DefaultPresetKey, preset.Id);

    // A new personal preset id derives from the given name, stays personal-
    // prefixed and never collides with a supplied or existing personal id.
    public static string UniquePersonalId(string name)
    {
        string slug = Slug(name);
        for (int attempt = 1; ; attempt++)
        {
            string candidate = attempt == 1 ? $"personal-{slug}" : $"personal-{slug}-{attempt}";
            if (All.All(choice => choice.Id != candidate))
                return candidate;
        }
    }

    private static string Slug(string name)
    {
        var builder = new StringBuilder();
        foreach (char letter in name.ToLowerInvariant())
            builder.Append(letter is >= 'a' and <= 'z' or >= '0' and <= '9' ? letter : '-');
        string slug = string.Join("-", builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length == 0)
            slug = "preset";
        return slug.Length > 34 ? slug[..34] : slug;
    }

    private static PersistentSettings? Settings => VisoLoomPlugIn.Instance?.Settings;

    private static IReadOnlyList<RepresentationPreset> Supplied()
    {
        var assembly = typeof(PromptPresets).Assembly;
        return new[] { "competition-sketch", "realistic", "architectural-sketch", "concept", "section-perspective", "material-study", "custom" }
            .Select(id =>
            {
                using var stream = assembly.GetManifestResourceStream($"VisoLoom.Presets.{id}.json")
                    ?? throw new InvalidOperationException("The supplied prompt preset is missing.");
                var preset = JsonSerializer.Deserialize<RepresentationPreset>(stream, WireJson.Options)
                    ?? throw new InvalidOperationException("The supplied prompt preset is invalid.");
                return preset.Id == "realistic" ? preset with { Name = "Photorealistic" } : preset;
            }).ToArray();
    }
}
