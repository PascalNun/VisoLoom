using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using VisoLoom.Tests;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class RenderImageStoreTests : IDisposable
{
    // macOS /var is a system symlink; exercise the same materialised-path rule
    // as the real private store rather than accidentally testing a linked root.
    private readonly string root = Path.Combine(PhysicalTemp(), "VisoLoom-image-save-" + Guid.NewGuid().ToString("N"));
    public RenderImageStoreTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);
    private static string PhysicalTemp()
    {
        string temp = Path.GetTempPath();
        return OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal)
            ? Path.Combine(new DirectoryInfo("/var").ResolveLinkTarget(true)!.FullName, temp[5..]) : temp;
    }

    [Fact]
    public void EveryCompletedImageGetsADurablePngWithoutPromptOrSceneExport()
    {
        string automatic = Path.Combine(root, "automatic"), selected = Path.Combine(root, "chosen");
        Directory.CreateDirectory(selected);
        var output = Output();
        var report = new RenderImageStore(automatic).Save(output, selected);
        Assert.Empty(report.Problems);
        Assert.Equal("Saved automatically.", report.Summary);
        Assert.Equal(output.PngBytes, File.ReadAllBytes(report.AutomaticPath!));
        Assert.Equal(output.PngBytes, File.ReadAllBytes(report.OutputPath!));
        Assert.Equal(report.OutputPath, report.SavedPath);
        Assert.All(Directory.GetFiles(root, "*", SearchOption.AllDirectories), p => Assert.EndsWith(".png", p));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(report.AutomaticPath!));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(automatic));
        }
    }

    [Fact]
    public void RecoveryIsIdempotentAndNeverOverwritesAnExistingDifferentImage()
    {
        var store = new RenderImageStore(Path.Combine(root, "automatic"));
        var output = Output();
        var first = store.Save(output);
        Assert.Equal(first.AutomaticPath, store.Save(output).AutomaticPath);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(first.AutomaticPath!)!));
        File.WriteAllText(first.AutomaticPath!, "Existing image must survive");
        var conflict = store.Save(output);
        Assert.Null(conflict.SavedPath);
        Assert.NotEmpty(conflict.Problems);
        Assert.Equal("Existing image must survive", File.ReadAllText(first.AutomaticPath!));
    }

    [Fact]
    public async Task ConcurrentRecoveryPreservesOneIdenticalFile()
    {
        var store = new RenderImageStore(Path.Combine(root, "automatic"));
        var output = Output();
        var reports = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => store.Save(output))));
        Assert.All(reports, report => Assert.Empty(report.Problems));
        Assert.Single(reports.Select(r => r.AutomaticPath).Distinct());
        Assert.Single(Directory.GetFiles(Path.Combine(root, "automatic")));
    }

    [Fact]
    public void MissingOutputDriveIsNotRecreatedOrSilentlyReplaced()
    {
        string missing = Path.Combine(root, "unmounted-drive", "renders");
        var report = new RenderImageStore(Path.Combine(root, "automatic")).Save(Output(), missing);
        Assert.NotNull(report.AutomaticPath);
        Assert.Null(report.OutputPath);
        Assert.NotEmpty(report.Problems);
        Assert.StartsWith("Saved automatically.\n", report.Summary);
        Assert.Contains(report.Problems[0], report.Summary);
        Assert.False(Directory.Exists(Path.Combine(root, "unmounted-drive")));
    }

    [Fact]
    public void OptionalCopyCanSucceedWhenTheAutomaticDiskCannotBeWritten()
    {
        string blocked = Path.Combine(root, "blocked"), chosen = Path.Combine(root, "chosen");
        File.WriteAllText(blocked, "not a directory");
        Directory.CreateDirectory(chosen);
        var report = new RenderImageStore(blocked).Save(Output(), chosen);
        Assert.Null(report.AutomaticPath);
        Assert.NotNull(report.OutputPath);
        Assert.NotEmpty(report.Problems);
        Assert.StartsWith("Saved to your chosen folder.\n", report.Summary);
        Assert.Contains(report.Problems[0], report.Summary);
    }

    [Fact]
    public void FailedSavingReportsNoSavedPathAndDoesNotChangeReturnedPixels()
    {
        string blocked = Path.Combine(root, "blocked");
        File.WriteAllText(blocked, "not a directory");
        var output = Output();
        var report = new RenderImageStore(blocked).Save(output, Path.Combine(root, "missing"));
        Assert.Null(report.SavedPath);
        Assert.Equal(2, report.Problems.Count);
        Assert.StartsWith("No saved copy.", report.Summary);
        Assert.All(report.Problems, problem => Assert.Contains(problem, report.Summary));
        Assert.Equal(SceneFixture.Png, output.PngBytes);
    }

    [Fact]
    public void MismatchedPixelsOrFrameAreRejectedBeforeAnyWrite()
    {
        string automatic = Path.Combine(root, "automatic");
        var store = new RenderImageStore(automatic);
        var output = Output();
        byte[] changed = output.PngBytes.ToArray(); changed[^1] ^= 1;
        Assert.Throws<ArgumentException>(() => store.Save(output with { PngBytes = changed }));
        Assert.Throws<ArgumentException>(() => store.Save(output with { Metadata = output.Metadata with { Plan = output.Metadata.Plan with { Final = new(4, 2) } } }));
        Assert.False(Directory.Exists(automatic));
    }

    [Fact]
    public void BoundedPrivateHistoryCanBeRemovedOnlyAfterItsImageIsPreserved()
    {
        string history = Path.Combine(root, "history"), automatic = Path.Combine(root, "automatic");
        Directory.CreateDirectory(history);
        var output = Output();
        File.WriteAllBytes(Path.Combine(history, "result.png"), output.PngBytes);
        File.WriteAllBytes(Path.Combine(history, "result.json"), JsonSerializer.SerializeToUtf8Bytes(output.Metadata, WireJson.Options));
        var store = new RenderImageStore(automatic);
        Assert.True(store.PreserveCompletedHistory(history));
        Directory.Delete(history, true);
        Assert.Equal(output.PngBytes, File.ReadAllBytes(Assert.Single(Directory.GetFiles(automatic))));
    }

    [Fact]
    public void IncompleteOrDamagedHistoryMustNotBeEvicted()
    {
        string history = Path.Combine(root, "history");
        Directory.CreateDirectory(history);
        var store = new RenderImageStore(Path.Combine(root, "automatic"));
        Assert.False(store.PreserveCompletedHistory(history));
        File.WriteAllText(Path.Combine(history, "result.json"), "{}");
        File.WriteAllBytes(Path.Combine(history, "result.png"), SceneFixture.Png);
        Assert.False(store.PreserveCompletedHistory(history));
        Assert.True(File.Exists(Path.Combine(history, "result.png")));
        var metadata = Output().Metadata;
        File.WriteAllBytes(Path.Combine(history, "result.json"), JsonSerializer.SerializeToUtf8Bytes(metadata with { Plan = null! }, WireJson.Options));
        Assert.False(store.PreserveCompletedHistory(history));
    }

    [Fact]
    public void AutomaticArchiveRejectsALinkedParentWithoutWritingThroughIt()
    {
        if (OperatingSystem.IsWindows()) return; // Native symlink creation needs developer privileges.
        string real = Path.Combine(root, "real"), link = Path.Combine(root, "link");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(link, real);
        var report = new RenderImageStore(Path.Combine(link, "archive")).Save(Output());
        Assert.Null(report.AutomaticPath);
        Assert.False(Directory.Exists(Path.Combine(real, "archive")));
    }

    private static ProviderResult Output()
    {
        var scene = SceneFixture.Create(); byte[] png = SceneFixture.Png;
        return new(png, new() { ResultId = Guid.NewGuid(), SceneId = scene.SceneId, SceneDigest = "fixture",
            ProviderId = "fixture", ModelId = "fixture", ModelVersion = "1", CompletedAt = DateTimeOffset.UtcNow,
            Plan = SceneFixture.Plan(scene.Frame), OutputSha256 = ContentDigest.Sha256(png), OutputByteCount = png.LongLength });
    }
}
