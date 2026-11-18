namespace Alycia.Modules.Chat.Domain;

public sealed class Attachment
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? ConversationId { get; set; }
    public Guid BatchId { get; set; }
    public Guid? ArchiveId { get; set; }
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Status { get; set; } = "";
    public string Text { get; set; } = "";
    public int AnchorSequence { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string SourceCreatedAt { get; set; } = "";
    public string SourceModifiedAt { get; set; } = "";
    public string MetadataSource { get; set; } = "unknown";
    public string ContextMode { get; set; } = "window";
}
