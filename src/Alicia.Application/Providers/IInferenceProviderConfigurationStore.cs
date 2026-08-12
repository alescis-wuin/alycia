namespace Alicia.Application.Providers;

public interface IInferenceProviderConfigurationStore
{
    Task<string?> LoadSelectedProviderIdAsync(
        CancellationToken cancellationToken = default);

    Task<InferenceProviderConfiguration?> LoadAsync(
        string providerId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        InferenceProviderConfiguration configuration,
        bool selectProvider,
        CancellationToken cancellationToken = default);
}
