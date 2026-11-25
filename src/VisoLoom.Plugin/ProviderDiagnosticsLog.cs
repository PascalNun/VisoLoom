using System.Text.Json;
using VisoLoom.Protocol;
using VisoLoom.Providers;

namespace VisoLoom.Plugin;

// A provider refusal is the one event the panel cannot explain beyond its
// bounded status sentence: the machine-readable reason rides on the
// ProviderException (RejectionDiagnostics) and is recorded here, into a
// machine-local file the user owns. This is no message surface — the panel
// keeps its sentence — and diagnostics must never affect a render: every
// failure in this writer stays silent. Path: <VisoLoom data>/logs/provider-rejections.jsonl.
internal static class ProviderDiagnosticsLog
{
    // One entry is bounded to match the provider's own 8 KB rejection cap;
    // the file keeps its recent tail when it passes this total.
    private const long MaximumFileBytes = 256 * 1024;
    private static readonly object gate = new();
    private static readonly JsonSerializerOptions Json = WireJson.CreateOptions();

    public static void Record(string provider, string model, ProviderException failure)
    {
        lock (gate)
        {
            try { TryAppend(provider, model, failure); }
            catch
            {
                // Diagnostics never surface as a second error of their own.
            }
        }
    }

    private static void TryAppend(string provider, string model, ProviderException failure)
    {
        string directory = Path.Combine(ApplicationDataRoot(), "VisoLoom", "logs");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "provider-rejections.jsonl");
        byte[] entry = SerializeEntry(provider, model, failure);
        if (File.Exists(path) && new FileInfo(path).Length >= MaximumFileBytes)
            KeepRecentTail(path);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(entry, 0, entry.Length);
        stream.Flush();
    }

    private static byte[] SerializeEntry(string provider, string model, ProviderException failure)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject();
            writer.WriteString("time"u8, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ssK"));
            writer.WriteString("provider"u8, provider);
            writer.WriteString("model"u8, model);
            writer.WriteString("code"u8, failure.Code);
            // The safe sentence carries the honest status (for example
            // "HTTP 400 · INVALID_ARGUMENT"); it is written, not invented.
            writer.WriteString("status"u8, failure.Message);
            if (failure.RejectionDiagnostics is { } diagnostics)
            {
                writer.WritePropertyName("providerBody"u8);
                // RawJSON keeps the provider's bounded body exactly as read.
                writer.WriteRawValue(diagnostics, skipInputValidation: false);
            }
            if (failure.ResponseShapeDiagnostics is { } shape)
            {
                writer.WritePropertyName("responseShape"u8);
                writer.WriteRawValue(shape, skipInputValidation: false);
            }
            writer.WriteEndObject();
        }
        return [.. bytes.ToArray(), .. "\n"u8.ToArray()];
    }

    private static void KeepRecentTail(string path)
    {
        // Keep the newest half of the bounded file: rejections stay appended
        // in order, so the tail is the recent history and rotation is lossless
        // beyond the cap.
        byte[] bytes = File.ReadAllBytes(path);
        int lineStart = bytes.Length / 2;
        while (lineStart > 0 && bytes[lineStart - 1] != (byte)'\n')
            lineStart--;
        using (FileStream overwritten = new FileStream(path + ".rotation", FileMode.Create, FileAccess.Write, FileShare.None))
            overwritten.Write(bytes, Math.Max(lineStart, 0), bytes.Length - Math.Max(lineStart, 0));
        File.Copy(path + ".rotation", path, overwrite: true);
        File.Delete(path + ".rotation");
    }

    private static string ApplicationDataRoot() => OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
}
