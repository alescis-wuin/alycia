namespace Alicia.Application.Providers;

public interface IInferenceProviderRuntime
{
    Task<InferenceProviderSnapshot> DetectAsync(
        CancellationToken cancellationToken = default);

    Task<InferenceProviderSnapshot> InstallAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<InferenceProviderSnapshot> StartAsync(
        InferenceProviderConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task<InferenceProviderSnapshot> StopAsync(
        CancellationToken cancellationToken = default);
}
