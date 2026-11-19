using System.Text.Json;
using VisoLoom.Core;
using VisoLoom.Protocol;

namespace VisoLoom.Plugin;

internal sealed record RenderModelProfile(string ProviderId, ModelCapabilities Capabilities, long? MaximumInputBytes = null)
{
    public ProjectModelPin Identity => new(ProviderId == "managed" ? "local" : ProviderId, Capabilities.Id, Capabilities.Version);

    public ResolutionPlan Plan(PixelSize size) => Plan(size, size);

    public ResolutionPlan Plan(PixelSize capture, PixelSize output) => Capabilities.IsDevelopmentPassthrough
        ? ResolutionPlanner.ReferencePlan(capture, output)
        : ResolutionPlanner.Plan(capture, output, Capabilities.Resolution);
}

internal sealed record ProjectModelPin(string ProviderId, string ModelId, string ModelVersion)
{
    public const string DocumentKey = "VisoLoom\\ModelPinV1";

    public static ProjectModelPin? Parse(string? json)
    {
        if (json is null || json.Length is 0 or > 1024)
            return null;
        try
        {
            var pin = JsonSerializer.Deserialize<ProjectModelPin>(json, WireJson.Options);
            return pin is not null && IsIdentifier(pin.ProviderId) && IsIdentifier(pin.ModelId) && IsIdentifier(pin.ModelVersion) ? pin : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsIdentifier(string? value) => value is { Length: > 0 and < 128 } &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' or ':' or '+' or '@');
}
