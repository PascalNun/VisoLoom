using System.Net;
namespace VisoLoom.Providers;

public static class EndpointPolicy
{
    public static void RequireLocalEndpoint(Uri endpoint)
    {
        // Literal loopback avoids DNS rebinding and accidental LAN/cloud disclosure. Office transport is deferred.
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https") ||
            !IPAddress.TryParse(endpoint.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/")
            throw new ArgumentException("Use an explicit loopback origin such as http://127.0.0.1:5179. Office and cloud connections are not enabled.");
    }

    public static string RequireOutputPath(string path, Guid jobId, string outputId)
    {
        var expected = $"/api/v1/jobs/{jobId:D}/outputs/{outputId}";
        if (outputId != "reference" || !string.Equals(path, expected, StringComparison.Ordinal))
            throw new ProviderException("unsafeOutputUrl", "The server returned an untrusted output location.", jobId);
        return expected;
    }
}
