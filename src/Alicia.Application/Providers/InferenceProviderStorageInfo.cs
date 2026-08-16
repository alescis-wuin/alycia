namespace Alicia.Application.Providers;

public sealed record InferenceProviderStorageInfo
{
    public InferenceProviderStorageInfo(
        bool hasManagedRuntime,
        string? managedVersion,
        int retainedReleaseCount,
        long runtimeBytes,
        long modelCacheBytes)
    {
        string? normalizedManagedVersion = string.IsNullOrWhiteSpace(managedVersion)
            ? null
            : managedVersion.Trim();

        if (hasManagedRuntime && normalizedManagedVersion is null)
        {
            throw new ArgumentException(
                "A managed provider runtime requires a managed version.",
                nameof(managedVersion));
        }

        if (!hasManagedRuntime && normalizedManagedVersion is not null)
        {
            throw new ArgumentException(
                "A provider without a managed runtime cannot expose a managed version.",
                nameof(managedVersion));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(retainedReleaseCount);

        ArgumentOutOfRangeException.ThrowIfNegative(runtimeBytes);

        ArgumentOutOfRangeException.ThrowIfNegative(modelCacheBytes);

        HasManagedRuntime = hasManagedRuntime;
        ManagedVersion = normalizedManagedVersion;
        RetainedReleaseCount = retainedReleaseCount;
        RuntimeBytes = runtimeBytes;
        ModelCacheBytes = modelCacheBytes;
    }

    public bool HasManagedRuntime { get; }

    public string? ManagedVersion { get; }

    public int RetainedReleaseCount { get; }

    public long RuntimeBytes { get; }

    public long ModelCacheBytes { get; }
}
