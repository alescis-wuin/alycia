using System.Collections.Immutable;

namespace Alycia.Contracts.Workspaces;

public sealed record ProjectEntry(Guid Id, string Name, bool Archived);
public sealed record ProjectNote(Guid Id, string Text, Guid? OriginConversationId, int? OriginSequence, DateTimeOffset UpdatedAtUtc);
public sealed record ProjectProfile(Guid Id, string Instructions, string DefaultModel, bool Archived, int Revision, ImmutableArray<ProjectNote> Notes);

public interface IProjectModelDefaults
{
    Task<string> ActiveDefaultModelAsync(Guid id, CancellationToken ct);
}

public interface IProjectCatalog : IProjectModelDefaults
{
    Task<IReadOnlyList<ProjectEntry>> ListAsync(CancellationToken ct);
    Task<ProjectEntry> CreateAsync(string name, CancellationToken ct);
    Task<ProjectProfile> GetAsync(Guid id, bool allowArchived, CancellationToken ct);
    Task<ProjectProfile> UpdateAsync(Guid id, int revision, string instructions, string defaultModel, bool archived, CancellationToken ct);
    Task<ProjectProfile> ChangeNoteAsync(Guid id, Guid? note, int revision, string text, Guid? originConversationId, int? originSequence, bool remove, CancellationToken ct);
}

// Enlisted in the caller's unit of work. An expected revision that has changed is rejected.
public interface IWorkspaceProfiles
{
    Task<ProjectProfile?> FindAsync(Guid id, CancellationToken ct);
    Task<ProjectProfile> RequireAsync(Guid id, bool allowArchived, CancellationToken ct);
    Task LockActiveDestinationAsync(Guid id, CancellationToken ct);
    Task AdvanceAsync(Guid id, int expectedRevision, CancellationToken ct);
}

// Conversations validates provenance in the unit of work that records the note.
public interface IProjectNoteOrigins
{
    Task ValidateAsync(Guid project, Guid? conversation, int? sequence, CancellationToken ct);
}
