namespace Alicia.Application.Providers;

public sealed record InferenceModelLibraryEntry
{
    public InferenceModelLibraryEntry(
        InferenceProviderConfiguration configuration,
        DateTimeOffset savedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.HasModelReference || configuration.ModelReference is null)
        {
            throw new ArgumentException(
                "A model-library entry requires a model reference.",
                nameof(configuration));
        }

        Configuration = new InferenceProviderConfiguration(
            configuration.ProviderId,
            configuration.ModelReference,
            configuration.ContextSize,
            configuration.Generation);
        SavedAtUtc = savedAtUtc.ToUniversalTime();
    }

    public InferenceProviderConfiguration Configuration { get; }

    public string ProviderId => Configuration.ProviderId;

    public string ModelReference => Configuration.ModelReference!;

    public DateTimeOffset SavedAtUtc { get; }
}
