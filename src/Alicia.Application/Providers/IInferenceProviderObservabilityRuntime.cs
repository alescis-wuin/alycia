namespace Alicia.Application.Providers;

public interface IInferenceProviderObservabilityRuntime
{
    InferenceProviderGenerationObservation? LatestGenerationObservation { get; }
}
