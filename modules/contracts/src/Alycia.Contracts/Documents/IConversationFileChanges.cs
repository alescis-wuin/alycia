namespace Alycia.Contracts.Documents;

public sealed record ConversationFileState(Guid Id, Guid? WorkspaceId, int Revision, int MessageCount);

// The owner stages a revision change on the same unit of work as the document edits.
public interface IConversationFileChanges
{
    Task<ConversationFileState> RequireAsync(Guid id, CancellationToken ct);
    Task StageAdvanceAsync(Guid id, int expectedRevision, CancellationToken ct);
}
