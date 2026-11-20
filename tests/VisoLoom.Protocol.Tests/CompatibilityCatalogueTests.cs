using System.Text;
using System.Text.Json;
using VisoLoom.Protocol;
using VisoLoom.Providers;
using Xunit;

namespace VisoLoom.Protocol.Tests;

public sealed class CompatibilityCatalogueTests
{
    [Fact]
    public void BundledCatalogueLoadsAndMatchesEveryCompiledProfile()
    {
        var catalogue = CompatibilityCatalogueStore.Current;
        Assert.NotNull(catalogue);
        Assert.True(catalogue!.Find("openai", OpenAiImageProvider.ModelId) is { Recommended: true });
        Assert.NotNull(catalogue.Find("local", ManagedModels.KleinFourBit.Id));
        Assert.NotNull(catalogue.Find("local", ManagedModels.KleinFourKLocal.Id));
        Assert.All(catalogue.Models, entry => Assert.False(entry.Retired));
    }

    [Fact]
    public void RecommendedPointsAtTheTestedOpenAIProfileAndNothingElse()
    {
        var catalogue = CompatibilityCatalogueStore.Current;
        Assert.Equal(OpenAiImageProvider.ModelId, RecommendationPolicy.Recommended(catalogue, "openai")!.ModelId);
        Assert.Null(RecommendationPolicy.Recommended(catalogue, "local"));
        Assert.Null(RecommendationPolicy.Recommended(catalogue, "managed"));
        Assert.Null(RecommendationPolicy.Recommended(null, "openai"));
    }

    [Fact]
    public void ChangeNoticeAppearsOnlyAfterASeenRecommendationActuallyChanges()
    {
        var catalogue = CompatibilityCatalogueStore.Current;
        Assert.Null(RecommendationPolicy.ChangeNotice(catalogue, "openai", lastSeenModelId: null, selectedModelId: "other"));
        Assert.Null(RecommendationPolicy.ChangeNotice(catalogue, "openai", lastSeenModelId: OpenAiImageProvider.ModelId, selectedModelId: "other"));
        Assert.Null(RecommendationPolicy.ChangeNotice(catalogue, "openai", lastSeenModelId: "older-model", selectedModelId: OpenAiImageProvider.ModelId));
        var notice = RecommendationPolicy.ChangeNotice(catalogue, "openai", lastSeenModelId: "older-model", selectedModelId: "other");
        Assert.NotNull(notice);
        Assert.Contains(RecommendationPolicy.Recommended(catalogue, "openai")!.Label, notice);
    }

    [Fact]
    public void AValidCatalogueParsesWithRevisionAndEntries()
    {
        var catalogue = CompatibilityCatalogueStore.Parse(Catalogue(Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, recommended: true)));
        Assert.Equal("1", catalogue.Revision);
        Assert.Equal(OpenAiImageProvider.ModelId, catalogue.Models.Single().ModelId);
    }

    [Fact]
    public void CatalogueDataMustStayWithinItsLimits()
    {
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "google", modelId: "unimplemented-model"))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "openai", modelId: "gpt-not-reviewed"))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "local", modelId: "flux9-unreviewed", profileRef: "mflux-0.0.0-unreviewed"))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId),
                      Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, recommended: true, retired: true))));
        // Both reviewed local profiles marked recommended: two recommendations for one provider.
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "local", modelId: ManagedModels.KleinFourBit.Id, profileRef: "mflux-0.22.0-klein-4b-q4", recommended: true),
                      Entry(providerId: "local", modelId: ManagedModels.KleinFourKLocal.Id, profileRef: "sdcpp-80c4707-klein-q4k", recommended: true))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, retired: true, successor: "missing-model"))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, label: new string('x', 81)))));
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, label: "label with \u0007 control"))));
        // The entry bound is checked before per-entry validation: 65 supported entries still fail.
        Assert.Throws<ArgumentException>(() => CompatibilityCatalogueStore.Parse(
            Catalogue(Enumerable.Range(0, 65).Select(_ => Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, recommended: true)).ToArray())));
        Assert.ThrowsAny<JsonException>(() => CompatibilityCatalogueStore.Parse("{ not json"u8.ToArray()));
    }

    [Fact]
    public void RetirementNamesACurrentCataloguedReplacement()
    {
        var successor = Entry(providerId: "openai", modelId: OpenAiImageProvider.ModelId, recommended: true);
        var retired = Entry(providerId: "openai", modelId: "gpt-retired-snapshot", retired: true, successor: OpenAiImageProvider.ModelId);
        var catalogue = CompatibilityCatalogueStore.Parse(Catalogue(successor, retired));
        Assert.True(catalogue.Find("openai", "gpt-retired-snapshot") is { Retired: true, SuccessorModelId: OpenAiImageProvider.ModelId });
    }

    [Fact]
    public void TryLoadReturnsNullForAnyInvalidOrMissingBundledCopy()
    {
        Assert.Null(CompatibilityCatalogueStore.TryLoad(typeof(object).Assembly));
        Assert.Null(CompatibilityCatalogueStore.TryLoad(typeof(CompatibilityCatalogueTests).Assembly));
    }

    private static byte[] Catalogue(params object[] models) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { format = 1, revision = "1", models }));

    private static object Entry(string providerId, string modelId, string? label = null, string? profileRef = null,
        bool recommended = false, bool retired = false, string? successor = null) => new
    {
        providerId,
        modelId,
        label = label ?? "Fixture model",
        profileRef = profileRef ?? "openai-edits-2026-10-11",
        recommended,
        retired,
        successorModelId = successor
    };
}
