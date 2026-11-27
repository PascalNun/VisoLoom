using System.Text.Json;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

internal sealed record ProjectRenderRow(Guid ResultId, string File, string Model, string ViewName,
    string DisplayStyle, int Width, int Height, DateTimeOffset CompletedAt, bool IsCaptureTest)
{
    public PixelSize Frame => new(Width, Height);
}

internal static class ProjectRenderIndex
{
    private const string LegacyKey = "VisoLoom\\RenderIndexV1";
    private const string Section = "VisoLoom.RenderHistoryV2";
    private const int MaximumLegacyLength = 8192;
    private const int MaximumEntryLength = 8192;

    // Each output has its own bounded entry. Reading history uses only Rhino
    // user text. PNGs are decoded on demand by the viewer.
    public static IReadOnlyList<ProjectRenderRow> Read(RhinoDoc? doc)
    {
        if (doc is null)
            return [];

        var rows = new Dictionary<Guid, ProjectRenderRow>();
        string? legacy = doc.Strings.GetValue(LegacyKey);
        if (legacy is { Length: > 0 and <= MaximumLegacyLength })
        {
            foreach (var row in ReadJson<ProjectRenderRow[]>(legacy) ?? [])
                if (IsValid(row))
                    rows[row.ResultId] = row;
        }

        foreach (string entry in doc.Strings.GetEntryNames(Section))
        {
            if (!Guid.TryParseExact(entry, "N", out var id))
                continue;
            string? json = doc.Strings.GetValue(Section, entry);
            if (json is not { Length: > 0 and <= MaximumEntryLength })
                continue;
            var row = ReadJson<ProjectRenderRow>(json);
            if (IsValid(row) && row.ResultId == id)
                rows[id] = row;
        }
        return rows.Values.OrderByDescending(row => row.CompletedAt).ToArray();
    }

    public static IReadOnlyList<ProjectRenderRow> Append(RhinoDoc? doc, ProjectRenderRow row,
        IReadOnlyList<ProjectRenderRow> existing)
    {
        var rows = new List<ProjectRenderRow>(existing.Count + 1) { row };
        rows.AddRange(existing.Where(item => item.ResultId != row.ResultId));
        if (doc is null || doc.IsReadOnly || !IsValid(row))
            return rows;

        string json = JsonSerializer.Serialize(row, WireJson.Options);
        if (json.Length > MaximumEntryLength)
            return rows;
        string entry = row.ResultId.ToString("N");
        doc.Strings.SetString(Section, entry, json);
        if (doc.Strings.GetValue(Section, entry) == json)
            doc.Modified = true;
        return rows;
    }

    private static T? ReadJson<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, WireJson.Options); }
        catch (JsonException) { return null; }
    }

    private static bool IsValid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ProjectRenderRow? row) =>
        row is not null && row.ResultId != Guid.Empty &&
        row.File is { Length: > 0 and <= 1024 } && Path.IsPathFullyQualified(row.File) &&
        row.File.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
        row.Model is { Length: > 0 and <= 128 } &&
        row.ViewName is { Length: <= 256 } &&
        row.DisplayStyle is { Length: <= 256 } &&
        row.Width is > 0 and <= 32768 && row.Height is > 0 and <= 32768 &&
        row.CompletedAt != default;
}
