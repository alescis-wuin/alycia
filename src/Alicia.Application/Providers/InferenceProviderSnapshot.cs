namespace Alicia.Application.Providers;

public sealed record InferenceProviderSnapshot
{
    public InferenceProviderSnapshot(
        string name,
        InferenceProviderState state,
        string? version = null,
        bool isCudaEnabled = false,
        string? executablePath = null,
        string? modelReference = null,
        Uri? endpoint = null,
        string? detail = null,
        InferenceProviderFailureKind? failureKind = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Provider name cannot be empty.", nameof(name));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (state == InferenceProviderState.Running && endpoint is null)
        {
            throw new ArgumentException(
                "A running provider must expose an endpoint.",
                nameof(endpoint));
        }

        InferenceProviderFailureKind? resolvedFailureKind = ResolveFailureKind(
            state,
            failureKind);

        Name = name.Trim();
        State = state;
        Version = NormalizeOptional(version);
        IsCudaEnabled = isCudaEnabled;
        ExecutablePath = NormalizeOptional(executablePath);
        ModelReference = NormalizeOptional(modelReference);
        Endpoint = endpoint;
        Detail = NormalizeOptional(detail);
        FailureKind = resolvedFailureKind;
    }

    public string Name { get; }

    public InferenceProviderState State { get; }

    public string? Version { get; }

    public bool IsCudaEnabled { get; }

    public string? ExecutablePath { get; }

    public string? ModelReference { get; }

    public Uri? Endpoint { get; }

    public string? Detail { get; }

    public InferenceProviderFailureKind? FailureKind { get; }

    private static InferenceProviderFailureKind? ResolveFailureKind(
        InferenceProviderState state,
        InferenceProviderFailureKind? failureKind)
    {
        InferenceProviderFailureKind? resolved = failureKind ?? state switch
        {
            InferenceProviderState.Missing => InferenceProviderFailureKind.Missing,
            InferenceProviderState.Unsupported => InferenceProviderFailureKind.Unsupported,
            InferenceProviderState.Faulted => InferenceProviderFailureKind.Faulted,
            _ => null,
        };

        bool isValid = state switch
        {
            InferenceProviderState.Missing => resolved == InferenceProviderFailureKind.Missing,
            InferenceProviderState.Unsupported => resolved == InferenceProviderFailureKind.Unsupported,
            InferenceProviderState.Faulted => resolved is InferenceProviderFailureKind.Faulted
                or InferenceProviderFailureKind.Network
                or InferenceProviderFailureKind.Model,
            _ => resolved is null,
        };

        if (!isValid)
        {
            throw new ArgumentException(
                $"Failure kind '{resolved}' is not valid for provider state '{state}'.",
                nameof(failureKind));
        }

        return resolved;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
