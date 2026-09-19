using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceModelLibraryTests
{
    [Fact]
    public void EntryRequiresModelReferenceAndNormalizesTimestampToUtc()
    {
        InferenceProviderConfiguration configuration = new(
            " provider.alpha ",
            " owner/model ",
            contextSize: 4096);
        DateTimeOffset localTimestamp = new(
            2026,
            9,
            19,
            20,
            0,
            0,
            TimeSpan.FromHours(2));

        InferenceModelLibraryEntry entry = new(configuration, localTimestamp);

        Assert.Equal("provider.alpha", entry.ProviderId);
        Assert.Equal("owner/model", entry.ModelReference);
        Assert.Equal(TimeSpan.Zero, entry.SavedAtUtc.Offset);
        Assert.Equal(localTimestamp.ToUniversalTime(), entry.SavedAtUtc);
        Assert.Throws<ArgumentException>(() => new InferenceModelLibraryEntry(
            new InferenceProviderConfiguration("provider.alpha"),
            localTimestamp));
    }

    [Fact]
    public void LibraryRejectsDuplicateProviderAndModelIdentity()
    {
        InferenceModelLibraryEntry first = CreateEntry(
            "provider.alpha",
            "owner/model",
            contextSize: 4096);
        InferenceModelLibraryEntry duplicate = CreateEntry(
            "provider.alpha",
            "owner/model",
            contextSize: 8192);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new InferenceModelLibrary([first, duplicate]));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithEntryAddsAndThenUpdatesWithoutDuplicatingModel()
    {
        InferenceModelLibraryEntry first = CreateEntry(
            "provider.alpha",
            "owner/model",
            contextSize: 4096);
        InferenceModelLibrary initial = new([first]);
        InferenceModelLibraryEntry updated = CreateEntry(
            "provider.alpha",
            "owner/model",
            contextSize: 8192);
        InferenceModelLibraryEntry second = CreateEntry(
            "provider.alpha",
            "owner/other-model",
            contextSize: null);

        InferenceModelLibrary result = initial
            .WithEntry(updated)
            .WithEntry(second);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(
            8192,
            result.Find("provider.alpha", "owner/model")?.Configuration.ContextSize);
        Assert.Same(second, result.Find("provider.alpha", "owner/other-model"));
    }

    [Fact]
    public void WithoutEntryRemovesOnlyExactProviderAndModelPair()
    {
        InferenceModelLibraryEntry alpha = CreateEntry(
            "provider.alpha",
            "owner/model",
            contextSize: 4096);
        InferenceModelLibraryEntry beta = CreateEntry(
            "provider.beta",
            "owner/model",
            contextSize: 8192);
        InferenceModelLibrary library = new([alpha, beta]);

        InferenceModelLibrary result = library.WithoutEntry(
            "provider.alpha",
            "owner/model");

        Assert.Null(result.Find("provider.alpha", "owner/model"));
        Assert.NotNull(result.Find("provider.beta", "owner/model"));
        Assert.Same(result, result.WithoutEntry("provider.alpha", "missing/model"));
    }

    private static InferenceModelLibraryEntry CreateEntry(
        string providerId,
        string modelReference,
        int? contextSize)
    {
        return new InferenceModelLibraryEntry(
            new InferenceProviderConfiguration(
                providerId,
                modelReference,
                contextSize,
                new InferenceGenerationOptions(temperature: 0.6)),
            new DateTimeOffset(2026, 9, 19, 18, 0, 0, TimeSpan.Zero));
    }
}
