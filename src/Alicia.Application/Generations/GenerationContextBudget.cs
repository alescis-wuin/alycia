using Alicia.Application.Providers;

namespace Alicia.Application.Generations;

public sealed record GenerationContextBudget
{
    public GenerationContextBudget(
        int? contextWindowTokens,
        int? reservedOutputTokens)
    {
        if (contextWindowTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contextWindowTokens),
                contextWindowTokens,
                "Context-window tokens must be positive when supplied.");
        }

        if (reservedOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reservedOutputTokens),
                reservedOutputTokens,
                "Reserved output tokens must be positive when supplied.");
        }

        if (contextWindowTokens is int window
            && reservedOutputTokens is int reserved
            && reserved >= window)
        {
            throw new ArgumentException(
                "Reserved output tokens must be smaller than the configured context window.",
                nameof(reservedOutputTokens));
        }

        ContextWindowTokens = contextWindowTokens;
        ReservedOutputTokens = reservedOutputTokens;
        MaximumInputTokens = contextWindowTokens is int knownWindow
            && reservedOutputTokens is int knownReserved
                ? knownWindow - knownReserved
                : null;
    }

    public int? ContextWindowTokens { get; }

    public int? ReservedOutputTokens { get; }

    public int? MaximumInputTokens { get; }

    public bool IsMaximumInputKnown => MaximumInputTokens is not null;

    public static GenerationContextBudget FromConfiguration(
        InferenceProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new GenerationContextBudget(
            configuration.ContextSize,
            configuration.Generation.MaxOutputTokens);
    }
}
