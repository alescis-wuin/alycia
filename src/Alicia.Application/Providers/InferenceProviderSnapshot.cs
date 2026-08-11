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
        string? detail = null)
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

        Name = name.Trim();
        State = state;
        Version = NormalizeOptional(version);
        IsCudaEnabled = isCudaEnabled;
        ExecutablePath = NormalizeOptional(executablePath);
        ModelReference = NormalizeOptional(modelReference);
        Endpoint = endpoint;
        Detail = NormalizeOptional(detail);
    }

    public string Name { get; }

    public InferenceProviderState State { get; }

    public string? Version { get; }

    public bool IsCudaEnabled { get; }

    public string? ExecutablePath { get; }

    public string? ModelReference { get; }

    public Uri? Endpoint { get; }

    public string? Detail { get; }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
