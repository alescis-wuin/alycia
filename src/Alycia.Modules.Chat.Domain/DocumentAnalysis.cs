namespace Alycia.Modules.Chat.Domain;

public sealed class DocumentAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public Guid? ScopeBatchId { get; set; }
    public string Title { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string SourceFingerprint { get; set; } = "";
    public string TreeText { get; set; } = "";
    public string SummaryText { get; set; } = "";
    public string ManifestJson { get; set; } = "";
    public int FileCount { get; set; }
    public int SummaryCalls { get; set; }
    public bool UseInContext { get; set; }
}
