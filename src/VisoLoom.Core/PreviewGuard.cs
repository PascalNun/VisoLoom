namespace VisoLoom.Core;

// A reviewed capture expires permanently when its inputs change. Submission is single-use.
public sealed class PreviewGuard(string fingerprint)
{
    public bool IsExpired { get; private set; }
    public bool IsSubmitted { get; private set; }

    public bool IsCurrent(string currentFingerprint)
    {
        IsExpired |= !string.Equals(fingerprint, currentFingerprint, StringComparison.Ordinal);
        return !IsExpired && !IsSubmitted;
    }

    public void Invalidate() => IsExpired = true;

    public void Submit(string currentFingerprint)
    {
        if (!IsCurrent(currentFingerprint))
            throw new InvalidOperationException("Preview the current capture before rendering.");
        IsSubmitted = true;
    }
}
