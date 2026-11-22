using VisoLoom.Core;
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
        Assert.Equal(10, profiles.Length);
        Assert.All(profiles, entry => Assert.NotNull(ManagedModels.Find(entry.EngineProfileId)));
        Assert.Equal(10, profiles.DistinctBy(entry => entry.EngineProfileId).Count());
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourBit.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourKLocal.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.KleinFourBNative.Id);
        // Every one-click download card names the profile its package serves.
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.Flux3KleinNative.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.Flux3KleinBaseNative.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.Flux2Klein9b.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.QwenImageEdit2511.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.LongcatImageEdit.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.Flux2Dev.Id);
        Assert.Contains(profiles, entry => entry.EngineProfileId == ManagedModels.LladaImageTurbo.Id);
    }

    [Fact]
    public void RecipesAndBoundsMirrorTheEngineProfiles()
    {
        // The dial and the worker read the same numbers; these are the
        // values the engine recipes freeze (engine/test_sdcpp_backend).
        Assert.Equal((4, 1, 16), (ManagedModels.KleinFourBNative.RecipeSteps, ManagedModels.KleinFourBNative.MinSteps, ManagedModels.KleinFourBNative.MaxSteps));
        Assert.Equal((16, 1, 32), (ManagedModels.KleinFourKLocal.RecipeSteps, ManagedModels.KleinFourKLocal.MinSteps, ManagedModels.KleinFourKLocal.MaxSteps));
        Assert.Equal((16, 1, 40), (ManagedModels.Flux3KleinNative.RecipeSteps, ManagedModels.Flux3KleinNative.MinSteps, ManagedModels.Flux3KleinNative.MaxSteps));
        Assert.Equal((20, 4, 60), (ManagedModels.Flux3KleinBaseNative.RecipeSteps, ManagedModels.Flux3KleinBaseNative.MinSteps, ManagedModels.Flux3KleinBaseNative.MaxSteps));
        Assert.Equal((4, 1, 16), (ManagedModels.Flux2Klein9b.RecipeSteps, ManagedModels.Flux2Klein9b.MinSteps, ManagedModels.Flux2Klein9b.MaxSteps));
        Assert.Equal((20, 4, 60), (ManagedModels.QwenImageEdit2511.RecipeSteps, ManagedModels.QwenImageEdit2511.MinSteps, ManagedModels.QwenImageEdit2511.MaxSteps));
        Assert.Equal((8, 1, 24), (ManagedModels.LongcatImageEdit.RecipeSteps, ManagedModels.LongcatImageEdit.MinSteps, ManagedModels.LongcatImageEdit.MaxSteps));
        Assert.Equal((20, 4, 60), (ManagedModels.Flux2Dev.RecipeSteps, ManagedModels.Flux2Dev.MinSteps, ManagedModels.Flux2Dev.MaxSteps));
    }

    [Fact]
    public void StepsOverrideFreezesIntoTheComposedIntentWithinBounds()
    {
        var definition = ManagedModels.Flux3KleinNative;
        var preset = new RepresentationPreset { Id = "p", Name = "p", BaseInstruction = "b" };
        var recipe = definition.Compose(preset, "user words", new PixelSize(128, 128));
        Assert.Null(recipe.SamplingSteps);
        Assert.Equal(definition.RecipeSteps, definition.Compose(preset, "", new PixelSize(128, 128), definition.RecipeSteps).SamplingSteps);
        Assert.Throws<ArgumentException>(() => definition.Compose(preset, "", new PixelSize(128, 128), definition.MaxSteps + 1));
        Assert.Throws<ArgumentException>(() => definition.Compose(preset, "", new PixelSize(128, 128), definition.MinSteps - 1));
    }

    [Fact]
    public void CatalogueRowsCarryTheirSourceAndIdsStayUnique()
    {
        var profileless = ModelCatalogue.Choices.Where(entry => entry.EngineProfileId is null).ToArray();
        var cloud = profileless.Where(entry => entry.Location == "Cloud").ToArray();
        Assert.Equal(4, cloud.Length);
        // Source honesty covers every local card: a reviewed package download
        // or the publisher, licence and weights record — LLaDA stays the one
        // publisher-link row until a quantised package is reviewed.
        var localCatalog = ModelCatalogue.Choices.Where(entry => entry.Location == "This computer").ToArray();
        Assert.Equal(10, localCatalog.Length);
        // Eight cards download their reviewed packages — LLaDA's quantised
        // review removed the last publisher-only row; the two prepared
        // installation rows state their sources without a package.
        Assert.Equal(8, localCatalog.Count(entry => entry.DownloadPackageId is not null));
        Assert.Contains(localCatalog, entry => entry.DownloadPackageId == "llada-image-turbo-gguf");
        Assert.Equal(2, localCatalog.Count(entry => entry.DownloadPackageId is null && entry.EngineProfileId is not null));
        Assert.All(localCatalog, entry => Assert.True(
            entry.PublisherUrl is not null && entry.LicenceUrl is not null && entry.WeightsNote is not null,
            "A local catalog row records where its weights come from."));
        Assert.All(localCatalog, entry => Assert.NotNull(entry.Fit));
        Assert.Equal(ModelCatalogue.Choices.Count, ModelCatalogue.Choices.DistinctBy(entry => entry.Id).Count());
    }
}
