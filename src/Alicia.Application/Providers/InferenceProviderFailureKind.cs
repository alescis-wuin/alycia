namespace Alicia.Application.Providers;

public enum InferenceProviderFailureKind
{
    Missing,
    Unsupported,
    Faulted,
    Network,
    Model,
}
