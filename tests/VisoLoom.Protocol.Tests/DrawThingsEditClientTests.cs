using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class DrawThingsEditClientTests
{
    [Theory]
    [InlineData("http://engine.example/")]
    [InlineData("http://192.168.1.10:7860/")]
    [InlineData("http://127.0.0.1:7860/?target=elsewhere")]
    public void CalibrationNeverConnectsToAnOfficeOrCloudAddress(string endpoint)
        => Assert.Throws<ArgumentException>(() => new DrawThingsEditClient(new(endpoint)));

    [Fact]
    public async Task WrongModelPreventsGenerationWithoutFallback()
    {
        using var handler = new Handler(_ => Json(new { model = "flux_2_klein_9b_q6p.ckpt" }));
        using var engine = new DrawThingsEditClient(new("http://127.0.0.1:7860/"), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => engine.EditAsync(Png(128, 128), Prompt()));
        Assert.Equal("modelUnavailable", error.Code);
        Assert.Equal(0, handler.Posts);
    }

    [Fact]
    public async Task EditIncludesTheExactImageAndShortPromptWithoutSceneJson()
    {
        byte[] image = Png(128, 128);
        byte[] expected = image.ToArray();
        string prompt = Prompt();
        using var handler = new Handler(request =>
        {
            Assert.Equal("127.0.0.1", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("/sdapi/v1/options", request.RequestUri.AbsolutePath);
                Assert.Null(request.Content);
                image[0] = 0; // Caller edits its buffer while discovery awaits; submission must stay frozen.
                return Json(new { model = DrawThingsEditClient.ModelId });
            }
            Assert.Equal("/sdapi/v1/img2img", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            var value = body.RootElement;
            Assert.Equal(prompt, value.GetProperty("prompt").GetString());
            Assert.Equal(DrawThingsEditClient.ModelId, value.GetProperty("model").GetString());
            Assert.Single(value.GetProperty("init_images").EnumerateArray());
            Assert.Equal(expected, Convert.FromBase64String(value.GetProperty("init_images")[0].GetString()!));
            Assert.Equal(128, value.GetProperty("width").GetInt32());
            Assert.Equal(4, value.GetProperty("steps").GetInt32());
            Assert.Equal(1, value.GetProperty("batch_size").GetInt32());
            Assert.Empty(value.GetProperty("controls").EnumerateArray());
            Assert.Empty(value.GetProperty("loras").EnumerateArray());
            Assert.False(value.TryGetProperty("scene", out _));
            return Json(new { images = new[] { Convert.ToBase64String(expected) } });
        });
        using var engine = new DrawThingsEditClient(new("http://127.0.0.1:7860/"), handler);
        Assert.Equal(expected, await engine.EditAsync(image, prompt));
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task InvalidFrameAndOverBudgetPromptNeverReachTheEngine()
    {
        using var handler = new Handler(_ => throw new Exception("No HTTP allowed."));
        using var engine = new DrawThingsEditClient(new("http://127.0.0.1:7860/"), handler);
        await Assert.ThrowsAsync<ArgumentException>(() => engine.EditAsync(Png(129, 128), Prompt()));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.EditAsync(Png(128, 128), Prompt() + new string('ä', 2048)));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("{\"images\":[\"http://external.invalid/output.png\"]}")]
    [InlineData("{\"images\":[]}")]
    [InlineData("[]")]
    public async Task InvalidOutputIsNotFetchedOrRetried(string body)
    {
        using var handler = new Handler(request => request.Method == HttpMethod.Get ?
            Json(new { model = DrawThingsEditClient.ModelId }) : new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        using var engine = new DrawThingsEditClient(new("http://127.0.0.1:7860/"), handler);
        Assert.Equal("invalidResult", (await Assert.ThrowsAsync<ProviderException>(() => engine.EditAsync(Png(128, 128), Prompt()))).Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ConnectionLossAfterSubmissionDoesNotRetryOrClaimCancellation()
    {
        using var handler = new Handler(request => request.Method == HttpMethod.Get ?
            Json(new { model = DrawThingsEditClient.ModelId }) : throw new HttpRequestException("private detail"));
        using var engine = new DrawThingsEditClient(new("http://127.0.0.1:7860/"), handler);
        var error = await Assert.ThrowsAsync<ProviderException>(() => engine.EditAsync(Png(128, 128), Prompt()));
        Assert.Equal("generationUnconfirmed", error.Code);
        Assert.DoesNotContain("private detail", error.Message);
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public void ComposerRetainsUserWordsAndDoesNotAppendTheScenePackage()
    {
        string prompt = DrawThingsEditClient.ComposePrompt(new()
        { Id = "test", Name = "Test", BaseInstruction = "Fine ink architectural sketch." }, "Warm daylight.");
        Assert.Contains("Warm daylight.", prompt);
        Assert.Contains(PreservationRules.Objective, prompt);
        Assert.DoesNotContain("Rhino scene context", prompt);
        Assert.Throws<ArgumentException>(() => DrawThingsEditClient.ComposePrompt(new()
        { Id = "test", Name = "Test", BaseInstruction = "Sketch." }, new string('x', 2048)));
    }

    private static string Prompt() => PreservationRules.Objective + "\nFine ink architectural sketch.";
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int Posts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (request.Method == HttpMethod.Post) Posts++;
            return Task.FromResult(respond(request));
        }
    }

    // A real decoded-size PNG, so the boundary checks are exercised without an image-library dependency.
    private static byte[] Png(int width, int height)
    {
        using var bytes = new MemoryStream();
        bytes.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 2;
        Chunk(bytes, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            zlib.Write(new byte[(width * 3 + 1) * height]);
        Chunk(bytes, "IDAT", compressed.ToArray());
        Chunk(bytes, "IEND", []);
        return bytes.ToArray();
    }
    private static void Chunk(Stream stream, string type, byte[] body)
    {
        byte[] name = Encoding.ASCII.GetBytes(type);
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, body.Length); stream.Write(word);
        stream.Write(name); stream.Write(body);
        uint crc = uint.MaxValue;
        foreach (byte b in name.Concat(body))
        {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); stream.Write(word);
    }
}
