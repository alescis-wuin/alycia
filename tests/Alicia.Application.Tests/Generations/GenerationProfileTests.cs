using System.Reflection;
using Alicia.Application.Generations;
using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Generations;

public sealed class GenerationProfileTests
{
    private static readonly string[] _expectedInitialSuggestions =
    {
        "Explain this code",
        "Review this change",
    };

    private static readonly string[] _invalidWhitespaceSuggestions =
    {
        "Valid",
        "   ",
    };

    private static readonly string[] _invalidNullSuggestions =
    {
        "Valid",
        null!,
    };

    [Fact]
    public void CustomProfileSnapshotsBehaviorDefensively()
    {
        GenerationProfileId id = GenerationProfileId.New();
        InferenceGenerationOptions options = new(
            maxOutputTokens: 768,
            temperature: 0.35,
            topP: 0.92,
            topK: 32,
            seed: 11,
            reasoningEnabled: true,
            reasoningBudgetTokens: 256);
        List<string> suggestions = new()
        {
            " Explain this code ",
            "Review this change",
        };
        const string instructions = "  Prefer precise, testable answers.\n";

        GenerationProfile profile = new(
            id,
            " Code ",
            instructions,
            options,
            suggestions);

        suggestions[0] = "Mutated after construction";

        Assert.Equal(id, profile.Id);
        Assert.Equal("Code", profile.Name);
        Assert.False(profile.IsDefault);
        Assert.Equal(instructions, profile.BaseSystemInstructions);
        Assert.Equal(options, profile.GenerationOptions);
        Assert.NotSame(options, profile.GenerationOptions);
        Assert.Equal(
            _expectedInitialSuggestions,
            profile.InitialSuggestions);
        Assert.False(profile.UsesProviderDefaults);
    }

    [Fact]
    public void DefaultProfileRepresentsNativeProviderBehavior()
    {
        GenerationProfileId id = GenerationProfileId.New();

        GenerationProfile profile = GenerationProfile.CreateDefault(id);

        Assert.Equal(id, profile.Id);
        Assert.Equal(GenerationProfile.DefaultProfileName, profile.Name);
        Assert.True(profile.IsDefault);
        Assert.Null(profile.BaseSystemInstructions);
        Assert.True(profile.GenerationOptions.UsesOnlyProviderDefaults);
        Assert.Empty(profile.InitialSuggestions);
        Assert.True(profile.UsesProviderDefaults);
    }

    [Fact]
    public void CustomProfileMayExplicitlyKeepAllProviderDefaults()
    {
        GenerationProfile profile = new(
            GenerationProfileId.New(),
            "Short answers");

        Assert.False(profile.IsDefault);
        Assert.Null(profile.BaseSystemInstructions);
        Assert.True(profile.GenerationOptions.UsesOnlyProviderDefaults);
        Assert.Empty(profile.InitialSuggestions);
        Assert.True(profile.UsesProviderDefaults);
    }

    [Fact]
    public void ProfileRejectsInvalidIdentityNameAndSuggestions()
    {
        GenerationProfileId id = GenerationProfileId.New();

        Assert.Throws<ArgumentException>(() =>
            new GenerationProfile(default, "Code"));
        Assert.Throws<ArgumentException>(() =>
            GenerationProfile.CreateDefault(default));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfile(id, "   "));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfile(id, "default"));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfile(
                id,
                "Code",
                initialSuggestions: _invalidWhitespaceSuggestions));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfile(
                id,
                "Code",
                initialSuggestions: _invalidNullSuggestions));
    }

    [Fact]
    public void ProfileExposesNoPubliclyWritableState()
    {
        PropertyInfo[] properties = typeof(GenerationProfile)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.NotEmpty(properties);
        Assert.All(
            properties,
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    [Fact]
    public void ProfileIdentifierUsesStableGuidRepresentation()
    {
        GenerationProfileId id = GenerationProfileId.New();

        Guid parsed = Guid.Parse(id.ToString());

        Assert.False(id.IsEmpty);
        Assert.Equal(id.Value, parsed);
        Assert.True(default(GenerationProfileId).IsEmpty);
    }
}
