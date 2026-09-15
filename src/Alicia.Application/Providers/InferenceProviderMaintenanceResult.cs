namespace Alicia.Application.Providers;

public sealed record InferenceProviderMaintenanceResult
{
    public InferenceProviderMaintenanceResult(
        InferenceProviderSnapshot snapshot,
        InferenceProviderStorageInfo storageInfo,
        long reclaimedBytes,
        string detail)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(storageInfo);

        ArgumentOutOfRangeException.ThrowIfNegative(reclaimedBytes);

        if (string.IsNullOrWhiteSpace(detail))
        {
            throw new ArgumentException(
                "Provider maintenance detail cannot be empty.",
                nameof(detail));
        }

        Snapshot = snapshot;
        StorageInfo = storageInfo;
        ReclaimedBytes = reclaimedBytes;
        Detail = detail.Trim();
    }

    public InferenceProviderSnapshot Snapshot { get; }

    public InferenceProviderStorageInfo StorageInfo { get; }

    public long ReclaimedBytes { get; }

    public string Detail { get; }
}
