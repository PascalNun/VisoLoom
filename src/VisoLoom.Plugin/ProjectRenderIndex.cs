using System.Text.Json;
using Rhino;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

// The render history that lives in the Rhino file itself: one small row per
// completed render naming the saved PNG's path and the render's provenance.
// The pixels stay on disk where they belong — the Rhino file never carries
// image bytes — and the project's renders are visible again the next time
// the file opens, exactly like the model pin (same document string, same
// save-and-verify pattern).
internal sealed record ProjectRenderRow(Guid ResultId, string File, string Model, string ViewName,
    string DisplayStyle, int Width, int Height, DateTimeOffset CompletedAt, bool IsCaptureTest)
{
    public PixelSize Frame => new(Width, Height);
}

internal static class ProjectRenderIndex
{
    public const string DocumentKey = "VisoLoom\\RenderIndexV1";

    // The panel shows three thumbnails; a bounded index keeps every reopen
    // cheap without pretending history is unlimited.
    private const int MaximumRows = 12;
    private const int MaximumJsonLength = 8192;

    public static IReadOnlyList<ProjectRenderRow> Parse(string? json)
    {
        if (json is null || json.Length is 0 or > MaximumJsonLength)
            return [];
        try
        {
            var rows = JsonSerializer.Deserialize<ProjectRenderRow[]>(json, WireJson.Options);
            return rows is null ? [] : rows.Where(IsValid).Take(MaximumRows).ToList();
        }
        catch (JsonException)
        {
            // A stale or corrupt index is better lost than fatal: the files
            // on disk are unaffected.
            return [];
        }
    }

    // Append happens on the render's completion, on the UI thread; a document
    // that vanished or is read-only keeps the render and simply skips the
    // index — reopen visibility degrades, nothing else.
    public static IReadOnlyList<ProjectRenderRow> Append(RhinoDoc? doc, ProjectRenderRow row,
        IReadOnlyList<ProjectRenderRow> existing)
    {
        var rows = new List<ProjectRenderRow> { row };
        rows.AddRange(existing.Where(item => item.ResultId != row.ResultId));
        while (rows.Count > MaximumRows)
            rows.RemoveAt(rows.Count - 1);
        if (doc is null || doc.IsReadOnly || !IsValid(row))
            return rows;
        string json = JsonSerializer.Serialize(rows, WireJson.Options);
        if (json.Length > MaximumJsonLength || Parse(json).Count != rows.Count)
            return rows;
        doc.Strings.SetString(DocumentKey, json);
        if (doc.Strings.GetValue(DocumentKey) != json)
            return rows;
        doc.Modified = true;
        return rows;
    }

    private static bool IsValid(ProjectRenderRow? row) => row is not null &&
        row.File is { Length: > 0 and <= 512 } && Path.IsPathFullyQualified(row.File) &&
        row.Model is { Length: > 0 and <= 128 } &&
        row.ViewName is { Length: > 0 and <= 256 } &&
        row.DisplayStyle is { Length: > 0 and <= 256 } &&
        row.Width is > 0 and <= 32768 && row.Height is > 0 and <= 32768 &&
        row.CompletedAt != default;
}
