namespace Alicia.Application.Providers;

public sealed record InferenceProviderConfiguration
{
    public InferenceProviderConfiguration(
        string providerId,
        string? modelReference = null,
        int? contextSize = null,
        InferenceGenerationOptions? generation = null)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider identifier cannot be empty.", nameof(providerId));
        }

        if (contextSize is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contextSize),
                "Context size must be greater than zero when specified.");
        }

        ProviderId = providerId.Trim();
        ModelReference = NormalizeOptional(modelReference);
        ContextSize = contextSize;
        Generation = generation ?? new InferenceGenerationOptions();
    }

    public string ProviderId { get; }

    public string? ModelReference { get; }

    public int? ContextSize { get; }

    public InferenceGenerationOptions Generation { get; }

    public bool HasModelReference => ModelReference is not null;

    public bool UsesProviderDefaults => ContextSize is null
        && Generation.UsesOnlyProviderDefaults;

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
