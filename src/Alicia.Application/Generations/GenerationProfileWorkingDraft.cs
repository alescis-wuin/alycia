namespace Alicia.Application.Generations;

public sealed class GenerationProfileWorkingDraft
{
    public GenerationProfileWorkingDraft(
        GenerationProfile profile,
        GenerationProfileRevisionId? baseRevisionId,
        DateTimeOffset updatedAt)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.IsDefault)
        {
            throw new ArgumentException(
                "The built-in default generation profile cannot have a working draft.",
                nameof(profile));
        }

        if (baseRevisionId is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Working-draft base revision identifier cannot be empty.",
                nameof(baseRevisionId));
        }

        Profile = GenerationProfileSnapshot.Copy(profile);
        BaseRevisionId = baseRevisionId;
        UpdatedAtUtc = updatedAt.ToUniversalTime();
    }

    public GenerationProfileId ProfileId => Profile.Id;

    public GenerationProfile Profile { get; }

    public GenerationProfileRevisionId? BaseRevisionId { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public bool IsNewProfile => BaseRevisionId is null;
}
