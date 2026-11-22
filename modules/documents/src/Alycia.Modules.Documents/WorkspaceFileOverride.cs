namespace Alycia.Modules.Documents;

public sealed class WorkspaceFileOverride
{
    public Guid ConversationId { get; set; }
    public Guid FileId { get; set; }
    public string Mode { get; set; } = "excluded";
}
