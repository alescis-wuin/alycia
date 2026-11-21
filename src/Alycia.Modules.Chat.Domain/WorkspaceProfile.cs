namespace Alycia.Modules.Chat.Domain;

// Historical EF mapping. Runtime profile access belongs to the Workspaces adapter.
public sealed class WorkspaceProfile
{
    public Guid Id { get; set; }
    public string Instructions { get; set; } = "";
    public string DefaultModel { get; set; } = "";
    public string NotesJson { get; set; } = "[]";
    public bool Archived { get; set; }
    public int Revision { get; set; }
}

public sealed class WorkspaceFileOverride
{
    public Guid ConversationId { get; set; }
    public Guid FileId { get; set; }
    public string Mode { get; set; } = "excluded";
}
