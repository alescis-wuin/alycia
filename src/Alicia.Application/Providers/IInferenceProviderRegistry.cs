namespace Alicia.Application.Providers;

public interface IInferenceProviderRegistry
{
    IReadOnlyList<InferenceProviderDescriptor> Providers { get; }

    string? SelectedProviderId { get; }

    void SelectProvider(string providerId);

    IInferenceProviderRuntime GetRequiredRuntime(string providerId);
}
