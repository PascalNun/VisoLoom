using VisoLoom.Core;
using Xunit;

namespace VisoLoom.Core.Tests;

public sealed class PreviewTests
{
    [Fact]
    public void ChangedInputsCannotReviveAnOldReviewByChangingBack()
    {
        var preview = new PreviewGuard("reviewed source and settings");
        Assert.True(preview.IsCurrent("reviewed source and settings"));
        Assert.False(preview.IsCurrent("changed source or settings"));
        Assert.False(preview.IsCurrent("reviewed source and settings"));
        Assert.Throws<InvalidOperationException>(() => preview.Submit("reviewed source and settings"));
    }

    [Fact]
    public void SubmissionIsSingleUseEvenWhenInputsStayTheSame()
    {
        var preview = new PreviewGuard("reviewed");
        preview.Submit("reviewed");
        Assert.True(preview.IsSubmitted);
        Assert.False(preview.IsCurrent("reviewed"));
        Assert.Throws<InvalidOperationException>(() => preview.Submit("reviewed"));
        Assert.True(new PreviewGuard("reviewed").IsCurrent("reviewed"));
    }

    [Fact]
    public void ExplicitRefreshRequirementRejectsUnchangedButInvalidatedCapture()
    {
        var preview = new PreviewGuard("reviewed");
        preview.Invalidate();
        Assert.True(preview.IsExpired);
        Assert.False(preview.IsCurrent("reviewed"));
        Assert.Throws<InvalidOperationException>(() => preview.Submit("reviewed"));
        Assert.False(preview.IsSubmitted);
    }
}
