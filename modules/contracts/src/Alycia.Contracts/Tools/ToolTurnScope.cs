namespace Alycia.Contracts.Tools;

// Values captured by the conversation owner in the current turn snapshot.
public sealed record ToolTurnScope(Guid ConversationId, Guid? WorkspaceId, string Model);
