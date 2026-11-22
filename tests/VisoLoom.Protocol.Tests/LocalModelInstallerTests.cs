using System.Diagnostics;
using System.Text.Json;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class LocalModelInstallerTests
{
    private static string AbsentConnection()
    {
        var path = Path.Combine(Path.GetTempPath(), "visoloom-installer-tests", "absent", "connection.json");
        return Path.GetFullPath(path);
    }

    [Fact]
    public async Task MissingRuntimeConfigurationRefusesWithTheLocalSetupMessage()
    {
        var installer = new LocalModelInstaller(AbsentConnection());
        var failure = await Assert.ThrowsAsync<ProviderException>(() => installer.ListPackagesAsync());
        Assert.Equal("runtimeMissing", failure.Code);
    }

    [Fact]
    public async Task RelativePathsAreRefusedBeforeAnyProcessStarts()
    {
        var installer = new LocalModelInstaller(AbsentConnection());
        await Assert.ThrowsAsync<ArgumentException>(() => installer.PlanAsync("flux2-klein-4b-gguf", "models/flux2"));
        await Assert.ThrowsAsync<ArgumentException>(() => installer.UninstallAsync("models/flux2"));
        await Assert.ThrowsAsync<ArgumentException>(() => installer.InstallAsync("flux2-klein-4b-gguf", "models/flux2"));
    }

    [Fact]
    public void ProgressLinesParseAsOneEventPerCompletedFile()
    {
        var line = """{"event":"fileComplete","name":"flux-2-klein-4b-Q4_0.gguf","index":1,"fileCount":3,"bytes":2460378560}""";
        var progress = LocalModelInstaller.ProgressFromLine(line);
        Assert.NotNull(progress);
        Assert.Equal("flux-2-klein-4b-Q4_0.gguf", progress!.Name);
        Assert.Equal(1, progress.Index);
        Assert.Equal(3, progress.FileCount);
        Assert.Equal(2_460_378_560L, progress.Bytes);
        Assert.Null(LocalModelInstaller.ResultFromLine(line));
    }

    [Fact]
    public void TheFinalRecordParsesOnlyWithoutAnEventProperty()
    {
        var line = """{"installed":["a.gguf","b.gguf","c.safetensors"],"destination":"/m/flux","digest":"abc"}""";
        var result = LocalModelInstaller.ResultFromLine(line);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Installed.Length);
        Assert.Equal("/m/flux", result.Destination);
        Assert.Equal("abc", result.Digest);
        Assert.Null(LocalModelInstaller.ProgressFromLine(line));
    }

    [Fact]
    public void AnythingElseIsNotARecognizedReport()
    {
        Assert.Null(LocalModelInstaller.ProgressFromLine("""{"event":"wake"}"""));
        Assert.Null(LocalModelInstaller.ResultFromLine("""{"started":true}"""));
        Assert.ThrowsAny<JsonException>(() => LocalModelInstaller.ProgressFromLine("not json"));
    }

    [Fact]
    public void EngineFailuresCarryTheirHonestReasonAndUnreadableReportsFailClosed()
    {
        var failure = LocalModelInstaller.FailureFromReport(
            "{\"error\":\"packageSpace\",\"message\":\"Not enough free space for the package and working slack.\"}",
            "installerUnavailable", "The model download stopped before finishing. Nothing was kept.");
        Assert.Equal("packageSpace", failure.Code);
        Assert.Equal("Not enough free space for the package and working slack.", failure.Message);
        var fallback = LocalModelInstaller.FailureFromReport("urlopen error noise", "installerUnavailable", "Nothing was kept.");
        Assert.Equal("installerUnavailable", fallback.Code);
        Assert.Equal("Nothing was kept.", fallback.Message);
    }

    [Fact]
    public void GatedModelRecordsRoundTripThroughTheWireNames()
    {
        // The engine reports a gated package as `requiresToken`; catalogue
        // JSON written before the field existed must keep parsing as open.
        var gated = JsonSerializer.Deserialize<ReviewedModelPackage>(
            """{"id":"flux3-pro","display":"Flux 3 Pro","purpose":"Refined renders","terms":"Per-model licence","profile":"flux-klein","totalBytes":2460378560,"fileCount":3,"requiresToken":true}""",
            WireJson.Options);
        Assert.True(gated!.RequiresToken);
        var open = JsonSerializer.Deserialize<ReviewedModelPackage>(
            """{"id":"flux2-klein-4b-gguf","display":"Flux 2 Klein 4B","purpose":"Fast drafts","terms":"Apache-2.0","profile":"flux-klein","totalBytes":5207177908,"fileCount":3}""",
            WireJson.Options);
        Assert.False(open!.RequiresToken);
    }

    [Fact]
    public async Task ABoundedInvocationReturnsTheReviewedCatalogueWhenTheEngineIsPresent()
    {
        // Exercises the real process boundary through the checkout's engine
        // script when python exists here; the engine suite covers machines
        // without one.
        var python = FindPython();
        var engine = FindEngineRoot();
        if (python is null || engine is null) return;
        var root = Path.Combine(Path.GetTempPath(), "visoloom-installer-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var configuration = JsonSerializer.Serialize(new
            { python, script = Path.Combine(engine, "engine.py") });
            var connection = Path.Combine(root, "connection.json");
            await File.WriteAllTextAsync(connection, configuration);
            var installer = new LocalModelInstaller(connection);
            var packages = await installer.ListPackagesAsync();
            Assert.Equal(2, packages.Count);
            var klein = Assert.Single(packages, package => package.Id == "flux2-klein-4b-gguf");
            Assert.Equal(5_207_177_908L, klein.TotalBytes);
            Assert.Equal(3, klein.FileCount);
            Assert.Equal("Apache-2.0", klein.Terms);
            Assert.False(klein.RequiresToken);
            var qwen = Assert.Single(packages, package => package.Id == "qwen-image-edit-2511-gguf");
            Assert.Equal(18_632_828_902L, qwen.TotalBytes);
            Assert.Equal(4, qwen.FileCount);
            Assert.Equal("Apache-2.0", qwen.Terms);
            Assert.False(qwen.RequiresToken);
            Assert.Empty(await installer.InstallsAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string? FindPython()
    {
        foreach (var name in new[] { "python3", "python" })
        {
            try
            {
                // The engine configuration requires absolute paths, so resolve
                // the interpreter before probing it.
                var resolve = Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "where.exe" : "which")
                {
                    ArgumentList = { name }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                });
                if (resolve is null) continue;
                var located = (resolve.StandardOutput.ReadToEnd() ?? "").Split('\n')[0].Trim();
                resolve.WaitForExit(5000);
                if (resolve.ExitCode != 0 || located.Length == 0 || !File.Exists(located)) continue;
                var probe = Process.Start(new ProcessStartInfo(located)
                {
                    ArgumentList = { "--version" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                });
                if (probe is null) continue;
                var output = probe.StandardOutput.ReadToEnd() + probe.StandardError.ReadToEnd();
                probe.WaitForExit(5000);
                if (probe.ExitCode == 0 && output.Contains("Python 3")) return located;
            }
            catch (Exception) { /* no python under this name here */ }
        }
        return null;
    }

    private static string? FindEngineRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "engine", "model_install.py")))
                return Path.Combine(directory.FullName, "engine");
            directory = directory.Parent;
        }
        return null;
    }
}
