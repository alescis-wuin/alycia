namespace Alicia.Application.Providers;

public sealed record InferenceProviderUpdateResult
{
    public InferenceProviderUpdateResult(
        InferenceProviderSnapshot snapshot,
        InferenceProviderUpdateInfo updateInfo)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(updateInfo);

        Snapshot = snapshot;
        UpdateInfo = updateInfo;
    }

    public InferenceProviderSnapshot Snapshot { get; }

    public InferenceProviderUpdateInfo UpdateInfo { get; }
}
