using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace VisoLoom.ReferenceServer;

internal sealed record MultipartSubmission(byte[] Request, byte[] Scene, byte[] Reference)
{
    public static async Task<MultipartSubmission> ReadAsync(HttpRequest request, ReferenceServerOptions options, CancellationToken cancellationToken)
    {
        if (request.ContentLength > options.MaximumRequestBytes)
            throw new ProtocolFailure(413, "requestTooLarge", "The upload exceeds the advertised byte limit.");
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new ProtocolFailure(415, "unsupportedMediaType", "Submit a multipart/form-data request.");
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 128)
            throw new ProtocolFailure(400, "invalidMultipart", "The multipart boundary is missing or too long.");

        var reader = new MultipartReader(boundary, new RequestLimitStream(request.Body, options.MaximumRequestBytes))
        {
            HeadersCountLimit = 8,
            HeadersLengthLimit = 8192,
            BodyLengthLimit = Math.Max(options.MaximumImageBytes, options.MaximumJsonBytes)
        };
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                if (parts.Count >= 3)
                    throw new ProtocolFailure(400, "invalidMultipart", "Only the declared request, scene, and reference image parts are supported.");
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) ||
                    !string.Equals(disposition.DispositionType.Value, "form-data", StringComparison.OrdinalIgnoreCase))
                    throw new ProtocolFailure(400, "invalidMultipart", "Each part must have a form-data disposition.");
                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (name is not ("request" or "scene" or "reference.png") || parts.ContainsKey(name))
                    throw new ProtocolFailure(400, "invalidMultipart", "A part is unknown or repeated.");
                var fileName = HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                var encodedFileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value;
                if ((fileName is not null && fileName != name && fileName != name + ".json") ||
                    (encodedFileName is not null && encodedFileName != fileName))
                    throw new ProtocolFailure(400, "invalidAsset", "Part filenames must match their declared logical name.");
                var expectedMime = name == "reference.png" ? "image/png" : "application/json";
                if (!MediaTypeHeaderValue.TryParse(section.ContentType, out var partType) ||
                    !string.Equals(partType.MediaType.Value, expectedMime, StringComparison.OrdinalIgnoreCase))
                    throw new ProtocolFailure(415, "unsupportedMediaType", "Each part must use its declared media type.");
                var limit = name == "reference.png" ? options.MaximumImageBytes : options.MaximumJsonBytes;
                using var data = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await section.Body.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    if (data.Length + read > limit)
                        throw new ProtocolFailure(413, "partTooLarge", "A part exceeds its advertised byte limit.");
                    await data.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                parts.Add(name, data.ToArray());
            }
        }
        catch (InvalidDataException)
        {
            throw new ProtocolFailure(400, "invalidMultipart", "The multipart body is malformed or exceeds its limits.");
        }
        if (!parts.TryGetValue("request", out var requestBytes) || !parts.TryGetValue("scene", out var scene) || !parts.TryGetValue("reference.png", out var reference))
            throw new ProtocolFailure(400, "missingPart", "The request, scene, and reference.png parts are required.");
        return new MultipartSubmission(requestBytes, scene, reference);
    }

    private sealed class RequestLimitStream(Stream inner, long maximum) : Stream
    {
        private long consumed;
        private void Count(int bytes)
        {
            consumed += bytes;
            if (consumed > maximum) throw new ProtocolFailure(413, "requestTooLarge", "The upload exceeds the advertised byte limit.");
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Count(read);
            return read;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
            Count(read);
            return read;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Count(read);
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
