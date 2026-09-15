namespace Alicia.Application.Providers;

public interface IInferenceProviderUpdateRuntime
{
    string ValidatedVersion { get; }

    Task<InferenceProviderUpdateInfo> CheckForUpdateAsync(
        CancellationToken cancellationToken = default);

    Task<InferenceProviderUpdateResult> UpdateAsync(
        IProgress<InferenceProviderProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
