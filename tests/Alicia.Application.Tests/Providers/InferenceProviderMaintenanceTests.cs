using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceProviderMaintenanceTests
{
    [Fact]
    public void StorageInfoNormalizesManagedVersionAndTracksIndependentScopes()
    {
        InferenceProviderStorageInfo info = new(
            hasManagedRuntime: true,
            managedVersion: " b10435 ",
            retainedReleaseCount: 2,
            runtimeBytes: 4096,
            modelCacheBytes: 8192);

        Assert.True(info.HasManagedRuntime);
        Assert.Equal("b10435", info.ManagedVersion);
        Assert.Equal(2, info.RetainedReleaseCount);
        Assert.Equal(4096, info.RuntimeBytes);
        Assert.Equal(8192, info.ModelCacheBytes);
    }

    [Fact]
    public void StorageInfoRejectsContradictoryRuntimeIdentity()
    {
        Assert.Throws<ArgumentException>(() => new InferenceProviderStorageInfo(
            hasManagedRuntime: true,
            managedVersion: null,
            retainedReleaseCount: 0,
            runtimeBytes: 0,
            modelCacheBytes: 0));

        Assert.Throws<ArgumentException>(() => new InferenceProviderStorageInfo(
            hasManagedRuntime: false,
            managedVersion: "b10435",
            retainedReleaseCount: 0,
            runtimeBytes: 0,
            modelCacheBytes: 0));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void StorageInfoRejectsNegativeMeasurements(
        int retainedReleaseCount,
        long runtimeBytes,
        long modelCacheBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceProviderStorageInfo(
            hasManagedRuntime: false,
            managedVersion: null,
            retainedReleaseCount,
            runtimeBytes,
            modelCacheBytes));
    }

    [Fact]
    public void MaintenanceResultRequiresSafeStructuredState()
    {
        InferenceProviderSnapshot snapshot = new(
            "Provider",
            InferenceProviderState.Missing,
            detail: "Managed runtime removed.");
        InferenceProviderStorageInfo storage = new(
            hasManagedRuntime: false,
            managedVersion: null,
            retainedReleaseCount: 0,
            runtimeBytes: 0,
            modelCacheBytes: 1234);

        InferenceProviderMaintenanceResult result = new(
            snapshot,
            storage,
            reclaimedBytes: 4096,
            detail: " Runtime removed; model cache preserved. ");

        Assert.Same(snapshot, result.Snapshot);
        Assert.Same(storage, result.StorageInfo);
        Assert.Equal(4096, result.ReclaimedBytes);
        Assert.Equal("Runtime removed; model cache preserved.", result.Detail);
        Assert.Throws<ArgumentOutOfRangeException>(() => new InferenceProviderMaintenanceResult(
            snapshot,
            storage,
            reclaimedBytes: -1,
            detail: "Invalid."));
    }

    [Fact]
    public void RemovalModesKeepRuntimeAndModelCacheScopesExplicit()
    {
        Assert.NotEqual(
            InferenceProviderRemovalMode.RuntimeOnly,
            InferenceProviderRemovalMode.RuntimeAndModelCache);
        Assert.True(Enum.IsDefined(InferenceProviderRemovalMode.RuntimeOnly));
        Assert.True(Enum.IsDefined(InferenceProviderRemovalMode.RuntimeAndModelCache));
    }
}
