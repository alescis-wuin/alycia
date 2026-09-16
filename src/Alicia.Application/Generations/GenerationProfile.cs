using System.Collections.ObjectModel;
using Alicia.Application.Providers;

namespace Alicia.Application.Generations;

public sealed class GenerationProfile
{
    public const string DefaultProfileName = "Default";

    private readonly ReadOnlyCollection<string> _initialSuggestions;

    public GenerationProfile(
        GenerationProfileId id,
        string name,
        string? baseSystemInstructions = null,
        InferenceGenerationOptions? generationOptions = null,
        IEnumerable<string>? initialSuggestions = null)
        : this(
            id,
            NormalizeCustomName(name),
            isDefault: false,
            baseSystemInstructions: NormalizeOptionalInstructions(baseSystemInstructions),
            generationOptions: CopyGenerationOptions(
                generationOptions ?? new InferenceGenerationOptions()),
            initialSuggestions: SnapshotSuggestions(initialSuggestions))
    {
    }

    private GenerationProfile(
        GenerationProfileId id,
        string name,
        bool isDefault,
        string? baseSystemInstructions,
        InferenceGenerationOptions generationOptions,
        string[] initialSuggestions)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile identifier cannot be empty.",
                nameof(id));
        }

        Id = id;
        Name = name;
        IsDefault = isDefault;
        BaseSystemInstructions = baseSystemInstructions;
        GenerationOptions = generationOptions;
        _initialSuggestions = Array.AsReadOnly(initialSuggestions);
    }

    public GenerationProfileId Id { get; }

    public string Name { get; }

    public bool IsDefault { get; }

    public string? BaseSystemInstructions { get; }

    public InferenceGenerationOptions GenerationOptions { get; }

    public IReadOnlyList<string> InitialSuggestions => _initialSuggestions;

    public bool UsesProviderDefaults => BaseSystemInstructions is null
        && GenerationOptions.UsesOnlyProviderDefaults;

    public static GenerationProfile CreateDefault(GenerationProfileId id)
    {
        return new GenerationProfile(
            id,
            DefaultProfileName,
            isDefault: true,
            baseSystemInstructions: null,
            generationOptions: new InferenceGenerationOptions(),
            initialSuggestions: Array.Empty<string>());
    }

    private static string NormalizeCustomName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Generation-profile name cannot be empty or whitespace.",
                nameof(name));
        }

        string normalized = name.Trim();

        if (string.Equals(
            normalized,
            DefaultProfileName,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"'{DefaultProfileName}' is reserved for the built-in default profile.",
                nameof(name));
        }

        return normalized;
    }

    private static string? NormalizeOptionalInstructions(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static InferenceGenerationOptions CopyGenerationOptions(
        InferenceGenerationOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new InferenceGenerationOptions(
            source.MaxOutputTokens,
            source.Temperature,
            source.TopP,
            source.TopK,
            source.Seed,
            source.ReasoningEnabled,
            source.ReasoningBudgetTokens);
    }

    private static string[] SnapshotSuggestions(IEnumerable<string>? suggestions)
    {
        if (suggestions is null)
        {
            return Array.Empty<string>();
        }

        List<string> snapshot = new();

        foreach (string? suggestion in suggestions)
        {
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                throw new ArgumentException(
                    "Generation-profile suggestions cannot contain empty values.",
                    nameof(suggestions));
            }

            snapshot.Add(suggestion.Trim());
        }

        return snapshot.ToArray();
    }
}
