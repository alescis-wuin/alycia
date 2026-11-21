using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record ProjectEntry(Guid Id, string Name, bool Archived);

public sealed record ProjectProfile(Guid Id, string Instructions, string DefaultModel, bool Archived, int Revision, IReadOnlyList<WorkspaceNote> Notes);

public interface IProjectCatalog
{
    Task<IReadOnlyList<ProjectEntry>> ListAsync(CancellationToken ct);
    Task<ProjectEntry> CreateAsync(string name, CancellationToken ct);
    Task<ProjectProfile> GetAsync(Guid id, bool allowArchived, CancellationToken ct);
    Task<string> ActiveDefaultModelAsync(Guid id, CancellationToken ct);
    Task<ProjectProfile> UpdateAsync(Guid id, int revision, string instructions, string defaultModel, bool archived, CancellationToken ct);
    Task<ProjectProfile> ChangeNoteAsync(Guid id, Guid? note, int revision, string text, Guid? originConversationId, int? originSequence, bool remove, CancellationToken ct);
    Task<Conversation> SetMembershipAsync(Guid id, int revision, Guid? workspaceId, CancellationToken ct);
}
