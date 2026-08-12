namespace Alicia.Application.Providers;

public sealed record InferenceGenerationOptions
{
    public InferenceGenerationOptions(
        int? maxOutputTokens = null,
        double? temperature = null,
        double? topP = null,
        int? topK = null,
        int? seed = null)
    {
        if (maxOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputTokens),
                "Maximum output tokens must be greater than zero when specified.");
        }

        if (temperature is < 0 || double.IsNaN(temperature ?? 0) || double.IsInfinity(temperature ?? 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                "Temperature must be a finite value greater than or equal to zero when specified.");
        }

        if (topP is < 0 or > 1 || double.IsNaN(topP ?? 0) || double.IsInfinity(topP ?? 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(topP),
                "Top-p must be between zero and one when specified.");
        }

        if (topK is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topK),
                "Top-k must be greater than or equal to zero when specified.");
        }

        if (seed is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seed),
                "Seed must be greater than or equal to zero when specified. Leave it unset for provider-random behavior.");
        }

        MaxOutputTokens = maxOutputTokens;
        Temperature = temperature;
        TopP = topP;
        TopK = topK;
        Seed = seed;
    }

    public int? MaxOutputTokens { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

    public int? TopK { get; }

    public int? Seed { get; }

    public bool UsesOnlyProviderDefaults => MaxOutputTokens is null
        && Temperature is null
        && TopP is null
        && TopK is null
        && Seed is null;
}
