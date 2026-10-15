using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;
using VisoLoom.Providers;

// Exercise the same local setup bridge used by Rhino Settings, without Rhino.
if (args.Length > 0 && args[0] is "library" or "inspect-folder")
{
    if (args[0] == "library" && args.Length != 1 || args[0] == "inspect-folder" && args.Length != 2)
    { Console.Error.WriteLine("library OR inspect-folder <absolute-model-folder>"); return 2; }
    try
    {
        var library = new LocalModelLibrary();
        object result = args[0] == "library" ? await library.ListAsync() : await library.InspectAsync(args[1]);
        Console.WriteLine(JsonSerializer.Serialize(result, WireJson.Options));
        return 0;
    }
    catch (ProviderException ex) { Console.Error.WriteLine(ex.Message); return 1; }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException or System.ComponentModel.Win32Exception)
    { Console.Error.WriteLine("The local model library could not be checked. No generation was started."); return 1; }
}

if (args.Length is not (2 or 4) || args[0] is not ("check" or "edit") ||
    args[0] == "check" && args.Length != 2 || args[0] == "edit" && args.Length != 4)
{
    Console.Error.WriteLine("check <loopback-origin> OR edit <loopback-origin> <prepared.png> <preset.json>");
    return 2;
}
try
{
    using var engine = new DrawThingsEditClient(new(args[1]));
    if (args[0] == "check")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await engine.CheckModelAsync(timeout.Token);
        Console.WriteLine("FLUX.2 klein 4B is selected. No image sent. This does not verify offloading is disabled.");
        return 0;
    }
    if (new FileInfo(args[3]).Length > 65_536 || new FileInfo(args[2]).Length > DrawThingsEditClient.MaximumImageBytes)
        throw new ArgumentException("Local test input exceeds its limits.");
    var preset = SubmissionValidator.Deserialize<RepresentationPreset>(await File.ReadAllBytesAsync(args[3]));
    string prompt = DrawThingsEditClient.ComposePrompt(preset, "");
    byte[] source = await File.ReadAllBytesAsync(args[2]);
    Console.WriteLine("Editing with the configured Draw Things engine. Stop waiting does not cancel its generation.");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    byte[] result = await engine.EditAsync(source, prompt);
    string directory = Path.GetFullPath(Path.Combine("artifacts", "local-flux", Guid.NewGuid().ToString("N")));
    if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
    else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    Write("reference.png", source);
    Write("result.png", result);
    Write("record.json", JsonSerializer.SerializeToUtf8Bytes(new
    {
        adapter = "drawthings-http-calibration-1",
        model = DrawThingsEditClient.ModelId,
        presetId = preset.Id,
        presetRevision = preset.Revision,
        prompt,
        size = PngHeader.Read(source),
        seed = 1,
        steps = 4,
        guidance = 1.0,
        strength = 1.0,
        sourceSha256 = ContentDigest.Sha256(source),
        outputSha256 = ContentDigest.Sha256(result),
        elapsedSeconds = watch.Elapsed.TotalSeconds,
        warnings = new[] { "Geometry must be compared with the reference.", "Some engine settings are inherited from Draw Things; this is a calibration experiment." }
    }, WireJson.Options));
    Console.WriteLine(directory);
    return 0;

    void Write(string name, byte[] bytes)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(Path.Combine(directory, name), options);
        file.Write(bytes);
    }
}
catch (ProviderException ex) { Console.Error.WriteLine(ex.Message); return 1; }
catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or ArgumentException or UnauthorizedAccessException or JsonException)
{ Console.Error.WriteLine("Local test could not complete. Check the engine, prepared PNG, preset and private output folder."); return 1; }
