using VisoLoom.Core;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class PersonalPresetStoreTests : IDisposable
{
    // macOS /var is a system symlink; exercise the same materialised-path rule
    // as the real store rather than accidentally testing a linked root.
    private readonly string root = Path.Combine(PhysicalTemp(), "VisoLoom-presets-" + Guid.NewGuid().ToString("N"));
    public PersonalPresetStoreTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);
    private static string PhysicalTemp()
    {
        string temp = Path.GetTempPath();
        return OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal)
            ? Path.Combine(new DirectoryInfo("/var").ResolveLinkTarget(true)!.FullName, temp[5..]) : temp;
    }

    private string StoreFile => Path.Combine(root, "personal.json");

    [Fact]
    public void ASavedCopyKeepsTheSuppliedStartingPointAndStampsItsRevision()
    {
        var store = new PersonalPresetStore(StoreFile);
        var first = store.Save(Preset());
        Assert.Equal("1", first.Revision);
        // A supplied starting point keeps its required passes; only identity and wording change.
        var stored = Assert.Single(store.Load().Presets);
        Assert.Equal("personal-atrium-ink", stored.Id);
        Assert.Equal("Atrium ink", stored.Name);
        Assert.Equal("Ink the visible edges.", stored.BaseInstruction);
        Assert.All(new[] { stored.RequiredPasses, stored.PreferredPasses }, passes => Assert.Equal([PassKind.Reference], passes));
        Assert.True(File.Exists(StoreFile));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StoreFile));
    }

    [Fact]
    public void SavingTheSameIdAgainReplacesItsContentAndAdvancesTheRevision()
    {
        var store = new PersonalPresetStore(StoreFile);
        store.Save(Preset());
        var edited = store.Save(Preset() with { BaseInstruction = "Ink the visible edges, darker." });
        Assert.Equal("2", edited.Revision);
        var load = store.Load();
        Assert.Null(load.Problem);
        var stored = Assert.Single(load.Presets);
        Assert.Equal("Ink the visible edges, darker.", stored.BaseInstruction);
    }

    [Fact]
    public void DeleteRemovesOnlyTheNamedPresetAndRejectsUnknownIds()
    {
        var store = new PersonalPresetStore(StoreFile);
        store.Save(Preset());
        store.Save(Preset() with { Id = "personal-night-glass", Name = "Night glass" });
        store.Delete("personal-atrium-ink");
        var remaining = Assert.Single(store.Load().Presets);
        Assert.Equal("personal-night-glass", remaining.Id);
        Assert.Throws<ArgumentException>(() => store.Delete("personal-atrium-ink"));
        Assert.Throws<ArgumentException>(() => store.Delete("competition-sketch"));
        store.Delete("personal-night-glass");
        Assert.Empty(store.Load().Presets);
    }

    [Fact]
    public void ACorruptOrOversizedFileFailsClosedToNoPersonalPresets()
    {
        var store = new PersonalPresetStore(StoreFile);
        Directory.CreateDirectory(root);
        System.IO.File.WriteAllText(StoreFile, "{not json");
        var corrupt = store.Load();
        Assert.Empty(corrupt.Presets);
        Assert.NotNull(corrupt.Problem);
        System.IO.File.WriteAllText(StoreFile, new string('a', 1_048_577));
        var oversized = store.Load();
        Assert.Empty(oversized.Presets);
        Assert.NotNull(oversized.Problem);
        System.IO.File.WriteAllText(StoreFile, "{\"schemaVersion\":\"9.9\",\"presets\":[]}");
        var future = store.Load();
        Assert.Empty(future.Presets);
        Assert.NotNull(future.Problem);
        System.IO.File.Delete(StoreFile);
        var missing = store.Load();
        Assert.Empty(missing.Presets);
        Assert.Null(missing.Problem);
        // A store that fails closed cannot be written into blindly.
        System.IO.File.WriteAllText(StoreFile, "{not json");
        Assert.Throws<IOException>(() => store.Save(Preset()));
        Assert.Throws<IOException>(() => store.Delete("personal-atrium-ink"));
    }

    [Fact]
    public void InvalidRecordsAreRejectedOnSaveAndFailTheWholeFileOnLoad()
    {
        var store = new PersonalPresetStore(StoreFile);
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { Id = "competition-sketch" }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { Id = "personal-" }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { Name = "" }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { BaseInstruction = "" }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { Description = new string('d', 401) }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { BaseInstruction = new string('i', 16_001) }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { RequiredPasses = [], PreferredPasses = [] }));
        Assert.Throws<ArgumentException>(() => store.Save(Preset() with { Revision = "0" }));
        Assert.False(File.Exists(StoreFile));
        System.IO.File.WriteAllText(StoreFile,
            "{\"schemaVersion\":\"1.0\",\"presets\":[{" +
            "\"schemaVersion\":\"1.0\",\"id\":\"personal-atrium-ink\",\"revision\":\"1\",\"name\":\"Atrium ink\"," +
            "\"baseInstruction\":\"Ink it.\",\"requiredPasses\":[\"reference\"],\"preferredPasses\":[\"reference\"]}," +
            "{\"schemaVersion\":\"1.0\",\"id\":\"personal-atrium-ink\",\"revision\":\"1\",\"name\":\"Duplicate\"," +
            "\"baseInstruction\":\"Ink it.\",\"requiredPasses\":[\"reference\"],\"preferredPasses\":[\"reference\"]}]}");
        var duplicated = store.Load();
        Assert.Empty(duplicated.Presets);
        Assert.NotNull(duplicated.Problem);
    }

    [Fact]
    public void WritesAreAtomicAndLeaveNoTemporaryFilesBehind()
    {
        var store = new PersonalPresetStore(StoreFile);
        store.Save(Preset());
        store.Save(Preset() with { BaseInstruction = "Ink, again." });
        Assert.All(Directory.GetFiles(root), path => Assert.Equal("personal.json", Path.GetFileName(path)));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
    }

    [Fact]
    public void AFullCollectionRemainsReadableWhenAnotherPresetIsAdded()
    {
        var presets = Enumerable.Range(0, 512).Select(index => Preset() with { Id = $"personal-{index}" }).ToArray();
        File.WriteAllBytes(StoreFile, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new { schemaVersion = "1.0", presets }, VisoLoom.Protocol.WireJson.Options));
        var store = new PersonalPresetStore(StoreFile);
        Assert.Throws<ArgumentException>(() => store.Save(Preset()));
        Assert.Null(store.Load().Problem);
        Assert.Equal(512, store.Load().Presets.Count);
        Assert.Equal("2", store.Save(presets[0]).Revision);
    }

    private static RepresentationPreset Preset() => new()
    {
        Id = "personal-atrium-ink",
        Name = "Atrium ink",
        Description = "A test description.",
        BaseInstruction = "Ink the visible edges.",
        PreservationIntent = "Preserve the captured composition.",
        VegetationPolicy = "Keep vegetation mass."
    };
}
