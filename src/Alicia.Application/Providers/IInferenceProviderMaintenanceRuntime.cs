namespace Alicia.Application.Providers;

public interface IInferenceProviderMaintenanceRuntime
{
    Task<InferenceProviderStorageInfo> InspectStorageAsync(
        CancellationToken cancellationToken = default);

    Task<InferenceProviderMaintenanceResult> CleanupRetainedReleasesAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<InferenceProviderMaintenanceResult> UninstallAsync(
        InferenceProviderRemovalMode mode,
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
