using System.Globalization;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed record ImageSaveReport(string? AutomaticPath, string? OutputPath, IReadOnlyList<string> Problems)
{
    public string? SavedPath => OutputPath ?? AutomaticPath;
    public string Summary
    {
        get
        {
            string saved = AutomaticPath is not null ? "Saved automatically." : OutputPath is not null
                ? "Saved to your chosen folder." : "No saved copy. Use Save PNG… to keep this image.";
            return Problems.Count == 0 ? saved : saved + "\n" + string.Join("\n", Problems);
        }
    }
}

// Completed PNGs are durable outputs, separate from bounded private scene/prompt
// history. A user-selected folder gets an additional PNG, never scene metadata.
public sealed class RenderImageStore(string automaticFolder)
{
    public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "renders");

    public ImageSaveReport Save(ProviderResult output, string? outputFolder = null)
    {
        if (output.PngBytes.Length > 67_108_864) throw new ArgumentException("The image exceeds the automatic save limit.");
        byte[] png = output.PngBytes.ToArray();
        Validate(png, output.Metadata);
        string name = "VisoLoom" + (output.Metadata.IsUnchangedReference ? "-reference" : "") + "-" +
            output.Metadata.CompletedAt.UtcDateTime.ToString("yyyy-MM-dd_HH-mm-ss'Z'", CultureInfo.InvariantCulture) + "-" + output.Metadata.ResultId.ToString("N") + ".png";
        List<string> problems = [];
        string? automatic = null, selected = null;
        try
        {
            if (!Path.IsPathFullyQualified(automaticFolder)) throw new IOException();
            for (var parent = new DirectoryInfo(automaticFolder); parent is not null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException();
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(automaticFolder);
            else
            {
                var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                Directory.CreateDirectory(automaticFolder, mode);
                File.SetUnixFileMode(automaticFolder, mode);
            }
            automatic = Write(automaticFolder, name, png);
        }
        catch (Exception ex) when (StorageFailure(ex))
        { problems.Add("The automatic image copy could not be saved."); }
        if (!string.IsNullOrEmpty(outputFolder))
        {
            try
            {
                // Never recreate a missing mounted drive/folder or silently use a different destination.
                if (!Path.IsPathFullyQualified(outputFolder) || !Directory.Exists(outputFolder)) throw new IOException();
                selected = Write(outputFolder, name, png);
            }
            catch (Exception ex) when (StorageFailure(ex))
            { problems.Add("The chosen output folder is unavailable or could not be written. Check it in Image saving settings."); }
        }
        return new(automatic, selected, problems);
    }

    // Before old private history is evicted, preserve any completed image in the
    // durable store. Unfinished/unreadable records must be left alone.
    public bool PreserveCompletedHistory(string directory)
    {
        try
        {
            string metadataPath = Path.Combine(directory, "result.json"), imagePath = Path.Combine(directory, "result.png");
            if (!Regular(metadataPath) || !Regular(imagePath) || new FileInfo(metadataPath).Length > 262144 || new FileInfo(imagePath).Length > 67_108_864)
                return false;
            var metadata = JsonSerializer.Deserialize<RenderResult>(File.ReadAllBytes(metadataPath), WireJson.Options);
            return metadata is not null && Save(new(File.ReadAllBytes(imagePath), metadata)).AutomaticPath is not null;
        }
        catch (Exception ex) when (StorageFailure(ex) || ex is JsonException)
        { return false; }
    }

    private static void Validate(byte[] png, RenderResult metadata)
    {
        if (metadata.Plan is null) throw new ArgumentException("The completed render has no recorded image frame.");
        ResolutionPlanner.ValidateSize(metadata.Plan.Final);
        if (png.Length > 67_108_864 || metadata.ResultId == Guid.Empty || png.LongLength != metadata.OutputByteCount ||
            PngHeader.Read(png) != metadata.Plan.Final || ContentDigest.Sha256(png) != metadata.OutputSha256)
            throw new ArgumentException("The image does not match its completed render record.");
    }

    private static string Write(string directory, string name, byte[] png)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException();
        string target = Path.Combine(directory, name);
        if (File.Exists(target))
        {
            if (SameImage(target, png)) return target;
            throw new IOException(); // Never replace an existing different image.
        }
        string temporary = Path.Combine(directory, ".visoloom-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(png); stream.Flush(true); }
            try { File.Move(temporary, target, overwrite: false); }
            catch (IOException) when (SameImage(target, png)) { } // Concurrent recovery of the same completed result.
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool SameImage(string path, byte[] png)
    {
        if (!Regular(path) || new FileInfo(path).Length != png.LongLength) return false;
        using var stream = File.OpenRead(path);
        var buffer = new byte[65536];
        int offset = 0;
        while (offset < png.Length)
        {
            int count = stream.Read(buffer, 0, Math.Min(buffer.Length, png.Length - offset));
            if (count == 0 || !buffer.AsSpan(0, count).SequenceEqual(png.AsSpan(offset, count))) return false;
            offset += count;
        }
        return stream.ReadByte() == -1;
    }
    private static bool Regular(string path) => File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    private static bool StorageFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
