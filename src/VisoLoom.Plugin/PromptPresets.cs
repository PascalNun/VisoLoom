using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

internal static class PromptPresets
{
    public static IReadOnlyList<RepresentationPreset> All { get; } = Load();
    private static IReadOnlyList<RepresentationPreset> Load()
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
