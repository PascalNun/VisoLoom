using System.Text.Json;
using System.Text.RegularExpressions;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Providers;

public sealed record PersonalPresetLoad(IReadOnlyList<RepresentationPreset> Presets, string? Problem)
{
    public static PersonalPresetLoad Empty { get; } = new([], null);
}

// Personal representation presets are the user's own data: one JSON document
// in application data, deliberately outside `.3dm`, project settings and Git.
// Every read validates shape and bounds, so a corrupt, oversized or partially
// invalid file fails closed to the supplied presets with the problem named,
// and never takes the panel or a render down. Writes are atomic and equally
// bounded. Revisions are stamped by the store, never trusted from the caller:
// job provenance depends on them distinguishing edited generations.
public sealed partial class PersonalPresetStore(string? file = null)
{
    public string FilePath { get; } = file ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisoLoom", "presets", "personal.json");
    private const int FileLimitBytes = 1_048_576;
    [GeneratedRegex("^personal-[a-z0-9-]{1,48}$")]
    private static partial Regex Identifier();

    private sealed record Document
    {
        public string SchemaVersion { get; init; } = "1.0";
        public RepresentationPreset[] Presets { get; init; } = [];
    }

    public PersonalPresetLoad Load()
    {
        var (presets, problem) = Read();
        return new(presets, problem);
    }

    // Adds a new personal preset or replaces the existing entry with the same
    // id, returning the stored record with its stamped revision.
    public RepresentationPreset Save(RepresentationPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        string problem = Validate(preset);
        if (problem.Length != 0)
            throw new ArgumentException(problem);
        var existing = ReadStrict();
        int index = existing.FindIndex(stored => stored.Id == preset.Id);
        string revision = index < 0 ? "1" : NextRevision(existing[index].Revision);
        var stamped = preset with { Revision = revision };
        if (index < 0)
            existing.Add(stamped);
        else
            existing[index] = stamped;
        Write(existing);
        return stamped;
    }

    public void Delete(string id)
    {
        if (id is null || !Identifier().IsMatch(id))
            throw new ArgumentException("Only personal presets can be deleted.");
        var existing = ReadStrict();
        if (existing.RemoveAll(stored => stored.Id == id) == 0)
            throw new ArgumentException("No personal preset with this id exists.");
        Write(existing);
    }

    // Missing file: no personal presets yet, that is not an error. Anything
    // else that cannot be read or trusted becomes a named problem, and the
    // supplied presets remain the only ones.
    private (List<RepresentationPreset> Presets, string? Problem) Read()
    {
        try
        {
            if (!File.Exists(FilePath))
                return ([], null);
            if (!Regular())
                return ([], "The personal presets file is not a regular file.");
            if (new FileInfo(FilePath).Length > FileLimitBytes)
                return ([], $"The personal presets file exceeds its {FileLimitBytes / (1 << 20)} MiB limit.");
            var document = SubmissionValidator.Deserialize<Document>(File.ReadAllBytes(FilePath));
            if (document.SchemaVersion != "1.0" || document.Presets is null)
                return ([], "The personal presets file is not a supported VisoLoom preset document.");
            if (document.Presets.Length > 512)
                return ([], "The personal presets file lists more presets than VisoLoom supports.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var preset in document.Presets)
            {
                if (preset is null || !seen.Add(preset.Id))
                    return ([], "The personal presets file contains an invalid or duplicate preset id.");
                string problem = Validate(preset);
                if (problem.Length != 0)
                    return ([], "A personal preset is invalid: " + problem);
            }
            return ([.. document.Presets], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return ([], "The personal presets file could not be read. It was ignored and the supplied presets are unchanged.");
        }
    }

    private List<RepresentationPreset> ReadStrict()
    {
        var (presets, problem) = Read();
        if (problem is { Length: > 0 })
            throw new IOException("The existing personal presets file could not be read, so nothing was changed. " + problem);
        return presets;
    }

    private void Write(List<RepresentationPreset> presets)
    {
        string json = JsonSerializer.Serialize(new Document { Presets = [.. presets] }, WireJson.Options);
        if (json.Length > FileLimitBytes)
            throw new ArgumentException("The personal presets would exceed the storage limit. Remove unused presets first.");
        string directory = Path.GetDirectoryName(FilePath)!;
        if (!Path.IsPathFullyQualified(FilePath))
            throw new IOException("The personal presets path must be absolute.");
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The personal presets folder is not a regular folder.");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            Directory.CreateDirectory(directory, mode);
            File.SetUnixFileMode(directory, mode);
        }
        string temporary = Path.Combine(directory, ".visoloom-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, options)) { stream.Write(System.Text.Encoding.UTF8.GetBytes(json)); stream.Flush(true); }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private bool Regular() => File.Exists(FilePath) && (File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) == 0;

    private static string NextRevision(string previous)
    {
        return int.TryParse(previous, out int revision) && revision is > 0 and < 999 ? (revision + 1).ToString() :
            throw new IOException("The stored preset revision is invalid, so the preset cannot be edited.");
    }

    // Personal presets are inputs to the composed prompt, so every field is
    // bounded the same way PromptComposer bounds the supplied ones. Personal
    // ids carry the "personal-" prefix so they can never collide with a
    // supplied preset id that a document pin or intent.json already recorded.
    private static string Validate(RepresentationPreset preset)
    {
        if (preset.Id is null || !Identifier().IsMatch(preset.Id))
            return "A personal preset id is required.";
        if (preset.SchemaVersion != "1.0")
            return "The preset schema version is not supported.";
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 100)
            return "The preset needs a name of up to 100 characters.";
        if (preset.Description?.Length > 400)
            return "The preset description must stay within 400 characters.";
        if (string.IsNullOrWhiteSpace(preset.BaseInstruction) || preset.BaseInstruction.Length > 16_000)
            return "The preset instruction is required and must stay within 16,000 characters.";
        if (preset.PreservationIntent?.Length > 4_000 || preset.VegetationPolicy?.Length > 4_000)
            return "The preservation detail fields must stay within 4,000 characters each.";
        if (preset.RequiredPasses is not { Count: > 0 } || preset.PreferredPasses is not { Count: > 0 })
            return "The preset must keep its required and preferred passes.";
        if (!int.TryParse(preset.Revision, out int revision) || revision is < 1 or > 999)
            return "The preset revision must be a number between 1 and 999.";
        return "";
    }
}
