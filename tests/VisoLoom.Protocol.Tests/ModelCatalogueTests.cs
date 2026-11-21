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
        Assert.Equal(2, profiles.Length);
        Assert.All(profiles, entry => Assert.NotNull(ManagedModels.Find(entry.EngineProfileId)));
        Assert.Equal(2, profiles.DistinctBy(entry => entry.EngineProfileId).Count());
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourBit.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourKLocal.Id);
    }

    [Fact]
    public void CloudChoicesNameNoEngineProfileAndChoiceIdsStayUnique()
    {
        var cloud = ModelCatalogue.Choices.Where(entry => entry.EngineProfileId is null).ToArray();
        Assert.Equal(3, cloud.Length);
        Assert.All(cloud, entry => Assert.Equal("Cloud", entry.Location));
        Assert.Equal(ModelCatalogue.Choices.Count, ModelCatalogue.Choices.DistinctBy(entry => entry.Id).Count());
    }
}
