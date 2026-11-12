using System.Runtime.InteropServices;
using System.Text.Json;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class ModelCatalogueTests
{
    [Theory]
    [InlineData("Windows", Architecture.X64, "unavailable")]
    [InlineData("macOS", Architecture.X64, "cannot run")]
    [InlineData("macOS", Architecture.Arm64, "Check the local connection")]
    public void HardwareGuidanceDoesNotPromiseUntestedReadiness(string system, Architecture architecture, string expected)
        => Assert.Contains(expected, ModelCatalogue.DescribeComputer(system, architecture));

    [Fact]
    public void SetupReportSeparatesAcceptedFilesFromLiveRenderingReadiness()
    {
        const string response = """
            {"id":"aabbccddeeff001122334455","state":"connectionCheckNeeded","message":"Check live connection",
             "modelAccepted":true,"sourceBytes":4619709935,"estimatedPreparedBytes":4620000000,
             "minimumPreparationFreeBytes":10737418240,
             "steps":[{"name":"Rhino connection","state":"checkRequired","message":"Check in Settings"}]}
            """;
        var report = JsonSerializer.Deserialize<LocalModelSetupPlan>(response, WireJson.Options)!;
        Assert.True(report.ModelAccepted);
        Assert.Equal("connectionCheckNeeded", report.State);
        Assert.Null(report.PreparationFreeBytes);
        Assert.Equal(10L << 30, report.MinimumPreparationFreeBytes);
        Assert.Equal("checkRequired", Assert.Single(report.Steps).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../../connection.json")]
    [InlineData("gggggggggggggggggggggggg")]
    public async Task InvalidSetupSelectionIsRejectedBeforeStartingAProcess(string id)
        => await Assert.ThrowsAsync<ArgumentException>(() => new LocalModelLibrary().PlanAsync(id));
}
