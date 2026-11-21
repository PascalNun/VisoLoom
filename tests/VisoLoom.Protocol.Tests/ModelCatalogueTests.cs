using VisoLoom.Protocol;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class ModelCatalogueTests
{
    [Fact]
    public void LocalChoicesEachBackOneReviewedAndDistinctEngineProfile()
    {
        var profiles = ModelCatalogue.Choices.Where(entry => entry.EngineProfileId is not null).ToArray();
        Assert.Equal(3, profiles.Length);
        Assert.All(profiles, entry => Assert.NotNull(ManagedModels.Find(entry.EngineProfileId)));
        Assert.Equal(3, profiles.DistinctBy(entry => entry.EngineProfileId).Count());
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourBit.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourKLocal.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourBNative.Id);
    }

    [Fact]
    public void CatalogueRowsCarryTheirSourceAndIdsStayUnique()
    {
        var profileless = ModelCatalogue.Choices.Where(entry => entry.EngineProfileId is null).ToArray();
        var cloud = profileless.Where(entry => entry.Location == "Cloud").ToArray();
        Assert.Equal(4, cloud.Length);
        var localCatalog = profileless.Where(entry => entry.Location == "This computer").ToArray();
        Assert.Equal(7, localCatalog.Length);
        var managed = localCatalog.Where(entry => entry.DownloadPackageId is not null).ToArray();
        Assert.Contains(managed, entry => entry.DownloadPackageId == "qwen-image-edit-2511-gguf");
        Assert.All(localCatalog, entry => Assert.True(
            entry.DownloadPackageId is not null || (entry.PublisherUrl is not null && entry.LicenceUrl is not null && entry.WeightsNote is not null),
            "A local catalog row either downloads a reviewed package or records where its weights come from."));
        Assert.All(localCatalog, entry => Assert.NotNull(entry.Fit));
        Assert.Equal(ModelCatalogue.Choices.Count, ModelCatalogue.Choices.DistinctBy(entry => entry.Id).Count());
    }
}
