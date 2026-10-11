using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Tests;

public static class SceneFixture
{
    public static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAD0lEQVR4nGP4z8DA8J8BAAf/Af8Bf4mnAAAAAElFTkSuQmCC");

    public static SceneManifest Create()
    {
        var view = Guid.NewGuid();
        return new()
        {
            SceneId = Guid.NewGuid(),
            CapturedAt = DateTimeOffset.UtcNow,
            Source = new()
            {
                ApplicationVersion = "synthetic",
                DocumentSessionId = "test-session",
                Units = "Meters",
                ModelTolerance = 0.001
            },
            Snapshot = new()
            {
                ConsistencyToken = "1",
                DocumentSessionId = "test-session",
                ViewportId = view
            },
            Frame = new(2, 1),
            Camera = new()
            {
                Position = new(0, 0, 10),
                Direction = new(0, 0, -1),
                Up = new(0, 1, 0),
                Projection = ProjectionKind.Perspective,
                Frustum = new(-2, 2, -1, 1, 1, 100),
                ViewportPixels = new(2, 1)
            },
            View = new()
            {
                ViewportId = view,
                DisplayModeId = Guid.NewGuid()
            },
            Passes = [
                new()
                {
                    Kind = PassKind.Reference,
                    Status = PassStatus.Available,
                    FileName = "reference.png",
                    MimeType = "image/png",
                    Size = new(2, 1),
                    BitDepth = 8,
                    Encoding = "sRGB",
                    Sha256 = ContentDigest.Sha256(Png),
                    ByteCount = Png.Length
                }
            ]
        };
    }

    public static ResolutionPlan Plan(PixelSize size) => new()
    {
        Capture = size,
        Native = size,
        Final = size,
        ContentRectangle = new(0, 0, size.Width, size.Height),
        Scale = 1
    };
}
