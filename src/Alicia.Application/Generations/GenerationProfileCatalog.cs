using System.Collections.ObjectModel;

namespace Alicia.Application.Generations;

public sealed class GenerationProfileCatalog
{
    private readonly ReadOnlyCollection<GenerationProfile> _profiles;
    private readonly ReadOnlyCollection<GenerationProfileRevision> _revisions;
    private readonly ReadOnlyCollection<GenerationProfileWorkingDraft> _workingDrafts;

    public GenerationProfileCatalog(
        GenerationProfileModelScope scope,
        GenerationProfile defaultProfile,
        IEnumerable<GenerationProfileRevision>? revisions = null,
        IEnumerable<GenerationProfileWorkingDraft>? workingDrafts = null)
    {
        if (scope.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile model scope cannot be empty.",
                nameof(scope));
        }

        ArgumentNullException.ThrowIfNull(defaultProfile);

        if (!defaultProfile.IsDefault)
        {
            throw new ArgumentException(
                "A generation-profile catalog requires the built-in default profile.",
                nameof(defaultProfile));
        }

        Scope = scope;
        DefaultProfile = GenerationProfileSnapshot.Copy(defaultProfile);

        GenerationProfileRevision[] revisionSnapshot = SnapshotRevisions(revisions);
        GenerationProfileWorkingDraft[] draftSnapshot = SnapshotDrafts(workingDrafts);
        ValidateRevisionHistory(DefaultProfile, revisionSnapshot, draftSnapshot);

        _revisions = Array.AsReadOnly(revisionSnapshot);
        _workingDrafts = Array.AsReadOnly(draftSnapshot);
        _profiles = Array.AsReadOnly(BuildCurrentProfiles(DefaultProfile, revisionSnapshot));
        ValidateCurrentProfileNames(_profiles);
    }

    public GenerationProfileModelScope Scope { get; }

    public GenerationProfile DefaultProfile { get; }

    public IReadOnlyList<GenerationProfile> Profiles => _profiles;

    public IReadOnlyList<GenerationProfileRevision> Revisions => _revisions;

    public IReadOnlyList<GenerationProfileWorkingDraft> WorkingDrafts => _workingDrafts;

    public static GenerationProfileCatalog CreateEmpty(
        GenerationProfileModelScope scope,
        GenerationProfileId defaultProfileId)
    {
        return new GenerationProfileCatalog(
            scope,
            GenerationProfile.CreateDefault(defaultProfileId));
    }

    public GenerationProfile? FindProfile(GenerationProfileId profileId)
    {
        if (profileId.IsEmpty)
        {
            return null;
        }

        if (profileId == DefaultProfile.Id)
        {
            return DefaultProfile;
        }

        GenerationProfileRevision? latestRevision = FindLatestRevision(profileId);
        return latestRevision?.Profile;
    }

    public GenerationProfileRevision? FindLatestRevision(GenerationProfileId profileId)
    {
        if (profileId.IsEmpty)
        {
            return null;
        }

        for (int index = _revisions.Count - 1; index >= 0; index--)
        {
            GenerationProfileRevision revision = _revisions[index];
            if (revision.ProfileId == profileId)
            {
                return revision;
            }
        }

        return null;
    }

    public GenerationProfileWorkingDraft? FindWorkingDraft(GenerationProfileId profileId)
    {
        if (profileId.IsEmpty)
        {
            return null;
        }

        foreach (GenerationProfileWorkingDraft draft in _workingDrafts)
        {
            if (draft.ProfileId == profileId)
            {
                return draft;
            }
        }

        return null;
    }

    public GenerationProfileCatalog WithWorkingDraft(GenerationProfileWorkingDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        List<GenerationProfileWorkingDraft> updatedDrafts = new(_workingDrafts.Count + 1);
        foreach (GenerationProfileWorkingDraft existing in _workingDrafts)
        {
            if (existing.ProfileId != draft.ProfileId)
            {
                updatedDrafts.Add(existing);
            }
        }

        updatedDrafts.Add(draft);
        return new GenerationProfileCatalog(
            Scope,
            DefaultProfile,
            _revisions,
            updatedDrafts);
    }

    public GenerationProfileCatalog WithoutWorkingDraft(GenerationProfileId profileId)
    {
        if (profileId.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile identifier cannot be empty.",
                nameof(profileId));
        }

        if (FindWorkingDraft(profileId) is null)
        {
            return this;
        }

        List<GenerationProfileWorkingDraft> updatedDrafts = new(_workingDrafts.Count);
        foreach (GenerationProfileWorkingDraft existing in _workingDrafts)
        {
            if (existing.ProfileId != profileId)
            {
                updatedDrafts.Add(existing);
            }
        }

        return new GenerationProfileCatalog(
            Scope,
            DefaultProfile,
            _revisions,
            updatedDrafts);
    }

    public GenerationProfileCatalog CommitWorkingDraft(
        GenerationProfileId profileId,
        GenerationProfileRevisionId revisionId,
        DateTimeOffset createdAt)
    {
        if (profileId.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile identifier cannot be empty.",
                nameof(profileId));
        }

        if (revisionId.IsEmpty)
        {
            throw new ArgumentException(
                "Generation-profile revision identifier cannot be empty.",
                nameof(revisionId));
        }

        GenerationProfileWorkingDraft draft = FindWorkingDraft(profileId)
            ?? throw new InvalidOperationException(
                "The generation profile does not have a working draft to commit.");
        GenerationProfileRevision? latestRevision = FindLatestRevision(profileId);

        if (latestRevision is null)
        {
            if (draft.BaseRevisionId is not null)
            {
                throw new InvalidOperationException(
                    "The working draft references a missing base revision.");
            }
        }
        else if (draft.BaseRevisionId != latestRevision.Id)
        {
            throw new InvalidOperationException(
                "The working draft is stale relative to the current confirmed profile revision.");
        }

        GenerationProfileRevision committedRevision = new(
            revisionId,
            draft.BaseRevisionId,
            createdAt,
            draft.Profile);
        List<GenerationProfileRevision> updatedRevisions = new(_revisions.Count + 1);
        updatedRevisions.AddRange(_revisions);
        updatedRevisions.Add(committedRevision);

        List<GenerationProfileWorkingDraft> updatedDrafts = new(_workingDrafts.Count);
        foreach (GenerationProfileWorkingDraft existing in _workingDrafts)
        {
            if (existing.ProfileId != profileId)
            {
                updatedDrafts.Add(existing);
            }
        }

        return new GenerationProfileCatalog(
            Scope,
            DefaultProfile,
            updatedRevisions,
            updatedDrafts);
    }

    private static GenerationProfileRevision[] SnapshotRevisions(
        IEnumerable<GenerationProfileRevision>? revisions)
    {
        if (revisions is null)
        {
            return Array.Empty<GenerationProfileRevision>();
        }

        List<GenerationProfileRevision> snapshot = new();
        foreach (GenerationProfileRevision? revision in revisions)
        {
            ArgumentNullException.ThrowIfNull(revision);
            snapshot.Add(new GenerationProfileRevision(
                revision.Id,
                revision.ParentRevisionId,
                revision.CreatedAtUtc,
                revision.Profile));
        }

        return snapshot.ToArray();
    }

    private static GenerationProfileWorkingDraft[] SnapshotDrafts(
        IEnumerable<GenerationProfileWorkingDraft>? drafts)
    {
        if (drafts is null)
        {
            return Array.Empty<GenerationProfileWorkingDraft>();
        }

        List<GenerationProfileWorkingDraft> snapshot = new();
        foreach (GenerationProfileWorkingDraft? draft in drafts)
        {
            ArgumentNullException.ThrowIfNull(draft);
            snapshot.Add(new GenerationProfileWorkingDraft(
                draft.Profile,
                draft.BaseRevisionId,
                draft.UpdatedAtUtc));
        }

        return snapshot.ToArray();
    }

    private static void ValidateRevisionHistory(
        GenerationProfile defaultProfile,
        IReadOnlyList<GenerationProfileRevision> revisions,
        IReadOnlyList<GenerationProfileWorkingDraft> drafts)
    {
        HashSet<GenerationProfileRevisionId> revisionIds = new();
        Dictionary<GenerationProfileRevisionId, GenerationProfileRevision> revisionsById = new();
        Dictionary<GenerationProfileId, GenerationProfileRevision> latestByProfile = new();

        foreach (GenerationProfileRevision revision in revisions)
        {
            if (!revisionIds.Add(revision.Id))
            {
                throw new ArgumentException(
                    "Generation-profile revision identifiers must be unique.",
                    nameof(revisions));
            }

            if (revision.ProfileId == defaultProfile.Id)
            {
                throw new ArgumentException(
                    "The built-in default generation profile cannot appear in revision history.",
                    nameof(revisions));
            }

            if (latestByProfile.TryGetValue(
                revision.ProfileId,
                out GenerationProfileRevision? previousRevision))
            {
                if (revision.ParentRevisionId != previousRevision.Id)
                {
                    throw new ArgumentException(
                        "Generation-profile revisions must form a contiguous linear chain per profile.",
                        nameof(revisions));
                }
            }
            else if (revision.ParentRevisionId is not null)
            {
                throw new ArgumentException(
                    "The first generation-profile revision must not have a parent.",
                    nameof(revisions));
            }

            revisionsById.Add(revision.Id, revision);
            latestByProfile[revision.ProfileId] = revision;
        }

        HashSet<GenerationProfileId> draftProfileIds = new();
        foreach (GenerationProfileWorkingDraft draft in drafts)
        {
            if (!draftProfileIds.Add(draft.ProfileId))
            {
                throw new ArgumentException(
                    "A generation profile can have at most one working draft.",
                    nameof(drafts));
            }

            if (draft.ProfileId == defaultProfile.Id)
            {
                throw new ArgumentException(
                    "The built-in default generation profile cannot have a working draft.",
                    nameof(drafts));
            }

            if (draft.BaseRevisionId is null)
            {
                if (latestByProfile.ContainsKey(draft.ProfileId))
                {
                    throw new ArgumentException(
                        "A working draft for an existing generation profile must reference a base revision.",
                        nameof(drafts));
                }

                continue;
            }

            if (!revisionsById.TryGetValue(
                draft.BaseRevisionId.Value,
                out GenerationProfileRevision? baseRevision)
                || baseRevision.ProfileId != draft.ProfileId)
            {
                throw new ArgumentException(
                    "A working draft must reference a revision of the same generation profile.",
                    nameof(drafts));
            }
        }
    }

    private static GenerationProfile[] BuildCurrentProfiles(
        GenerationProfile defaultProfile,
        IReadOnlyList<GenerationProfileRevision> revisions)
    {
        List<GenerationProfileId> profileOrder = new();
        Dictionary<GenerationProfileId, GenerationProfile> latestProfiles = new();

        foreach (GenerationProfileRevision revision in revisions)
        {
            if (latestProfiles.TryAdd(revision.ProfileId, revision.Profile))
            {
                profileOrder.Add(revision.ProfileId);
            }
            else
            {
                latestProfiles[revision.ProfileId] = revision.Profile;
            }
        }

        List<GenerationProfile> profiles = new(profileOrder.Count + 1)
        {
            GenerationProfileSnapshot.Copy(defaultProfile),
        };

        foreach (GenerationProfileId profileId in profileOrder)
        {
            profiles.Add(GenerationProfileSnapshot.Copy(latestProfiles[profileId]));
        }

        return profiles.ToArray();
    }

    private static void ValidateCurrentProfileNames(
        IEnumerable<GenerationProfile> profiles)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (GenerationProfile profile in profiles)
        {
            if (!names.Add(profile.Name))
            {
                throw new ArgumentException(
                    "Current generation-profile names must be unique within one model catalog.",
                    nameof(profiles));
            }
        }
    }
}
