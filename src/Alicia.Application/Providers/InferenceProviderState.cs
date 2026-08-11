namespace Alicia.Application.Providers;

public enum InferenceProviderState
{
    Detecting,
    Missing,
    Ready,
    Installing,
    Starting,
    Running,
    Stopping,
    Unsupported,
    Faulted,
}
