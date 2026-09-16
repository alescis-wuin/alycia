using System.Reflection;
using Alicia.Application.Generations;
using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Generations;

public sealed class GenerationProfileCatalogTests
{
    [Fact]
    public void ModelScopeNormalizesProviderAndModelIdentity()
    {
        GenerationProfileModelScope scope = new(
            " provider.alpha ",
            " owner/model-GGUF:Q4_K_M ");

        Assert.Equal("provider.alpha", scope.ProviderId);
        Assert.Equal("owner/model-GGUF:Q4_K_M", scope.ModelReference);
        Assert.False(scope.IsEmpty);
        Assert.True(default(GenerationProfileModelScope).IsEmpty);
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfileModelScope("   ", "model"));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfileModelScope("provider", "   "));
    }

    [Fact]
    public void RevisionSnapshotsPayloadAndComputesStableHash()
    {
        GenerationProfileId profileId = GenerationProfileId.New();
        GenerationProfileRevisionId revisionId = GenerationProfileRevisionId.New();
        InferenceGenerationOptions options = new(
            maxOutputTokens: 512,
            temperature: 0.25,
            topP: 0.9,
            topK: 32,
            seed: 7,
            reasoningEnabled: true,
            reasoningBudgetTokens: 128);
        List<string> suggestions = new()
        {
            "Explain this code",
            "Review the tests",
        };
        GenerationProfile source = new(
            profileId,
            "Code",
            "Prefer precise implementation details.",
            options,
            suggestions);
        DateTimeOffset localTime = new(2026, 9, 16, 22, 30, 0, TimeSpan.FromHours(2));

        GenerationProfileRevision revision = new(
            revisionId,
            parentRevisionId: null,
            localTime,
            source);
        GenerationProfileRevision equivalent = new(
            GenerationProfileRevisionId.New(),
            parentRevisionId: null,
            localTime.AddMinutes(1),
            source);

        suggestions[0] = "mutated";

        Assert.Equal(revisionId, revision.Id);
        Assert.Equal(profileId, revision.ProfileId);
        Assert.Null(revision.ParentRevisionId);
        Assert.Equal(localTime.ToUniversalTime(), revision.CreatedAtUtc);
        Assert.NotSame(source, revision.Profile);
        Assert.NotSame(options, revision.Profile.GenerationOptions);
        Assert.Equal("Explain this code", revision.Profile.InitialSuggestions[0]);
        Assert.Equal(64, revision.PayloadHash.Length);
        Assert.Equal(revision.PayloadHash, equivalent.PayloadHash);
    }

    [Fact]
    public void RevisionHashChangesWithBehavioralPayload()
    {
        GenerationProfileId profileId = GenerationProfileId.New();
        GenerationProfile firstProfile = new(
            profileId,
            "Code",
            generationOptions: new InferenceGenerationOptions(temperature: 0.2));
        GenerationProfile secondProfile = new(
            profileId,
            "Code",
            generationOptions: new InferenceGenerationOptions(temperature: 0.3));

        GenerationProfileRevision first = new(
            GenerationProfileRevisionId.New(),
            parentRevisionId: null,
            DateTimeOffset.UtcNow,
            firstProfile);
        GenerationProfileRevision second = new(
            GenerationProfileRevisionId.New(),
            first.Id,
            DateTimeOffset.UtcNow,
            secondProfile);

        Assert.NotEqual(first.PayloadHash, second.PayloadHash);
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfileRevision(
                GenerationProfileRevisionId.New(),
                parentRevisionId: null,
                DateTimeOffset.UtcNow,
                GenerationProfile.CreateDefault(GenerationProfileId.New())));
    }

    [Fact]
    public void CatalogCommitsNewWorkingDraftAsInitialRevision()
    {
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            scope,
            GenerationProfileId.New());
        GenerationProfile customProfile = new(
            GenerationProfileId.New(),
            "Code",
            "Be concise.");
        DateTimeOffset draftTime = DateTimeOffset.UtcNow;
        GenerationProfileWorkingDraft draft = new(
            customProfile,
            baseRevisionId: null,
            draftTime);

        GenerationProfileCatalog withDraft = catalog.WithWorkingDraft(draft);
        GenerationProfileRevisionId revisionId = GenerationProfileRevisionId.New();
        GenerationProfileCatalog committed = withDraft.CommitWorkingDraft(
            customProfile.Id,
            revisionId,
            draftTime.AddMinutes(1));

        Assert.Single(withDraft.WorkingDrafts);
        Assert.Empty(withDraft.Revisions);
        Assert.Empty(committed.WorkingDrafts);
        GenerationProfileRevision revision = Assert.Single(committed.Revisions);
        Assert.Equal(revisionId, revision.Id);
        Assert.Null(revision.ParentRevisionId);
        Assert.Equal(customProfile.Id, revision.ProfileId);
        Assert.Equal(2, committed.Profiles.Count);
        Assert.Equal("Code", committed.FindProfile(customProfile.Id)?.Name);
    }

    [Fact]
    public void CatalogLinksSubsequentCommittedRevisionToCurrentRevision()
    {
        GenerationProfileCatalog catalog = GenerationProfileCatalog.CreateEmpty(
            new GenerationProfileModelScope("provider.alpha", "owner/model"),
            GenerationProfileId.New());
        GenerationProfileId profileId = GenerationProfileId.New();
        GenerationProfileWorkingDraft initialDraft = new(
            new GenerationProfile(profileId, "Code"),
            baseRevisionId: null,
            DateTimeOffset.UtcNow);
        GenerationProfileRevisionId firstRevisionId = GenerationProfileRevisionId.New();
        catalog = catalog
            .WithWorkingDraft(initialDraft)
            .CommitWorkingDraft(
                profileId,
                firstRevisionId,
                DateTimeOffset.UtcNow.AddMinutes(1));
        GenerationProfileWorkingDraft updatedDraft = new(
            new GenerationProfile(
                profileId,
                "Code",
                generationOptions: new InferenceGenerationOptions(temperature: 0.4)),
            firstRevisionId,
            DateTimeOffset.UtcNow.AddMinutes(2));
        GenerationProfileRevisionId secondRevisionId = GenerationProfileRevisionId.New();

        GenerationProfileCatalog updated = catalog
            .WithWorkingDraft(updatedDraft)
            .CommitWorkingDraft(
                profileId,
                secondRevisionId,
                DateTimeOffset.UtcNow.AddMinutes(3));

        Assert.Equal(2, updated.Revisions.Count);
        Assert.Equal(firstRevisionId, updated.Revisions[1].ParentRevisionId);
        Assert.Equal(secondRevisionId, updated.FindLatestRevision(profileId)?.Id);
        Assert.Equal(0.4, updated.FindProfile(profileId)?.GenerationOptions.Temperature);
    }

    [Fact]
    public void CatalogKeepsStaleDraftButRefusesToCommitIt()
    {
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfile defaultProfile = GenerationProfile.CreateDefault(
            GenerationProfileId.New());
        GenerationProfileId profileId = GenerationProfileId.New();
        GenerationProfileRevision first = new(
            GenerationProfileRevisionId.New(),
            parentRevisionId: null,
            DateTimeOffset.UtcNow,
            new GenerationProfile(profileId, "Code"));
        GenerationProfileWorkingDraft staleDraft = new(
            new GenerationProfile(
                profileId,
                "Code",
                generationOptions: new InferenceGenerationOptions(temperature: 0.2)),
            first.Id,
            DateTimeOffset.UtcNow.AddMinutes(1));
        GenerationProfileRevision second = new(
            GenerationProfileRevisionId.New(),
            first.Id,
            DateTimeOffset.UtcNow.AddMinutes(2),
            new GenerationProfile(
                profileId,
                "Code",
                generationOptions: new InferenceGenerationOptions(temperature: 0.3)));
        GenerationProfileCatalog catalog = new(
            scope,
            defaultProfile,
            new List<GenerationProfileRevision> { first, second },
            new List<GenerationProfileWorkingDraft> { staleDraft });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            catalog.CommitWorkingDraft(
                profileId,
                GenerationProfileRevisionId.New(),
                DateTimeOffset.UtcNow.AddMinutes(3)));

        Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(catalog.FindWorkingDraft(profileId));
    }

    [Fact]
    public void CatalogRejectsBrokenChainsDuplicateNamesAndInvalidDraftBases()
    {
        GenerationProfileModelScope scope = new("provider.alpha", "owner/model");
        GenerationProfile defaultProfile = GenerationProfile.CreateDefault(
            GenerationProfileId.New());
        GenerationProfileId firstProfileId = GenerationProfileId.New();
        GenerationProfileRevision first = new(
            GenerationProfileRevisionId.New(),
            parentRevisionId: null,
            DateTimeOffset.UtcNow,
            new GenerationProfile(firstProfileId, "Code"));
        GenerationProfileRevision broken = new(
            GenerationProfileRevisionId.New(),
            GenerationProfileRevisionId.New(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new GenerationProfile(firstProfileId, "Code"));
        GenerationProfileRevision duplicateName = new(
            GenerationProfileRevisionId.New(),
            parentRevisionId: null,
            DateTimeOffset.UtcNow,
            new GenerationProfile(GenerationProfileId.New(), "code"));
        GenerationProfileWorkingDraft invalidBase = new(
            new GenerationProfile(GenerationProfileId.New(), "Creative"),
            first.Id,
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            new GenerationProfileCatalog(
                scope,
                defaultProfile,
                new List<GenerationProfileRevision> { first, broken }));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfileCatalog(
                scope,
                defaultProfile,
                new List<GenerationProfileRevision> { first, duplicateName }));
        Assert.Throws<ArgumentException>(() =>
            new GenerationProfileCatalog(
                scope,
                defaultProfile,
                new List<GenerationProfileRevision> { first },
                new List<GenerationProfileWorkingDraft> { invalidBase }));
    }

    [Fact]
    public void CatalogAndRevisionContractsExposeNoPubliclyWritableState()
    {
        Type[] immutableTypes =
        {
            typeof(GenerationProfileRevision),
            typeof(GenerationProfileWorkingDraft),
            typeof(GenerationProfileCatalog),
        };

        foreach (Type type in immutableTypes)
        {
            PropertyInfo[] properties = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public);

            Assert.NotEmpty(properties);
            Assert.All(
                properties,
                property => Assert.False(property.SetMethod?.IsPublic ?? false));
        }

        GenerationProfileRevisionId revisionId = GenerationProfileRevisionId.New();
        Assert.False(revisionId.IsEmpty);
        Assert.Equal(revisionId.Value, Guid.Parse(revisionId.ToString()));
        Assert.True(default(GenerationProfileRevisionId).IsEmpty);
    }
}
