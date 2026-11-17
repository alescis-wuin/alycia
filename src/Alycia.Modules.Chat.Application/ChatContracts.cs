using Alycia.Modules.Chat.Domain;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public interface IChatRepository
{
    Task<IReadOnlyList<Attachment>> FilesAsync(Guid id, CancellationToken cancellationToken);
    async Task<IReadOnlyList<Attachment>> ContextFilesAsync(Guid id, int first, CancellationToken ct) =>
        (await FilesAsync(id, ct)).Where(f => f.ContextMode != "excluded"
            && ((f.ContextMode == "pinned" || f.ContextMode == "project") || f.AnchorSequence >= first)).ToArray();
    async Task<IReadOnlyList<ChatMessage>> SummaryRangeAsync(Guid id, int after, int through, CancellationToken ct) =>
        (await MessagesAsync(id, through - after, through + 1, ct)).Where(m => m.Sequence > after && m.Sequence <= through).ToArray();
    Task<IReadOnlyList<Conversation>> ListAsync(CancellationToken cancellationToken);
    Task<Conversation?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(Guid id, int limit, int? before, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> FindTurnAsync(Guid id, Guid requestId, CancellationToken cancellationToken);
    Task AppendTurnAsync(Conversation conversation, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken);
}

public sealed record PromptMessage(string Role, string Content,
    [property: JsonPropertyName("tool_calls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ModelToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ToolName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Thinking = null);
public sealed record ContextWindow(IReadOnlyList<PromptMessage> Messages, int HistoryMessagesIncluded, int EstimatedInputTokens, int FilesIncluded = 0, bool NeedsSummary = false, int SummaryThroughSequence = 0, string SummaryText = "", int SummaryCalls = 0, IReadOnlyList<SourceExcerpt>? Sources = null, int FilesAvailable = 0, int ToolReserve = 0, RetrievalReport? Retrieval = null, MemoryReport? Memory = null);
public sealed record GenerationStats(int? PromptTokens, int? OutputTokens, long? TotalNanoseconds,
    long? EvaluationNanoseconds, string? DoneReason, long? PromptEvaluationNanoseconds = null, long? LoadNanoseconds = null, int? CachedPromptTokens = null);
public sealed record GenerationReply(string Content, GenerationStats? Stats = null, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? Thinking = null, IReadOnlyList<ToolObservation>? Tools = null);
public sealed record ModelDelta(string Content, string? Thinking = null, string? Status = null, ToolObservation? Tool = null);
public sealed record SendMessageResult(IReadOnlyList<ChatMessage> Messages, int? HistoryMessagesIncluded,
    bool Replayed, int FilesIncluded = 0, int? EstimatedInputTokens = null, GenerationStats? Stats = null, GenerationUsage? Usage = null);

public interface IChatModel
{
    Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions options, CancellationToken ct) => Task.FromResult(options.Copy());
    async Task<GenerationReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct) => new(await GenerateAsync(model, messages, options, ct));
    Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => Task.FromResult(false);
    Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        throw new ChatModelException("Ce fournisseur ne prend pas en charge les outils.");
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings, CancellationToken ct)
        => GenerateAsync(model, messages, ct);
    async Task<GenerationReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct)
    {
        var content = await GenerateAsync(model, messages, settings, ct);
        await onDelta(new(content));
        return new(content);
    }
}

public sealed class ChatConflictException() : Exception("La conversation a changé. Rechargez-la puis réessayez.");
public sealed class ConversationNotFoundException() : Exception("Conversation introuvable.");
public sealed class ChatModelException(string message) : Exception(message);

public sealed class ChatOptions
{
    public bool StructuredMemory { get; set; } = true;
    [JsonIgnore] public System.Text.Json.JsonElement? ResponseSchema { get; set; }
    public string RetrievalMode { get; set; } = "lexical";
    public string EmbeddingModel { get; set; } = "";
    [JsonIgnore] public IReadOnlyList<SourceChunk>? SourceCandidates { get; set; }
    [JsonIgnore] public RetrievalReport? RetrievalReport { get; set; }
    public string TokenCounting { get; set; } = "auto";
    [JsonIgnore] public ITextTokenCounter? TokenCounter { get; set; }
    [JsonIgnore] public string? ModelIdentity { get; set; }
    [JsonIgnore] public string CallPurpose { get; set; } = "chat";
    public bool UseProjectTools { get; set; } = true;
    public int ToolMaxCalls { get; set; } = 24;
    public ChatOptions Copy() => (ChatOptions)MemberwiseClone();
    public bool AutoSummarize { get; set; } = true;
    public int SummaryMaxCalls { get; set; } = 256;
    public bool GenerateUntilStop { get; set; } = true;
    public int MaxHistoryTurns { get; set; } = 12;
    public int ContextTokens { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 1024;
    public int SafetyTokens { get; set; } = 256;
    public int TimeoutSeconds { get; set; } = 180;
    public double Temperature { get; set; } = 0.7;
    public int? Seed { get; set; }

    public void Validate()
    {
        if (RetrievalMode is not ("legacy" or "lexical" or "hybrid") || EmbeddingModel is null || EmbeddingModel.Length > 200 || TokenCounting is not ("auto" or "conservative") || ToolMaxCalls is < 1 or > 128 || SummaryMaxCalls is < 8 or > 2048 || MaxHistoryTurns is < 0 or > 100 || ContextTokens is < 1024 or > 131072
            || MaxOutputTokens < 1 || SafetyTokens < 128
            || (long)MaxOutputTokens + SafetyTokens >= ContextTokens
            || TimeoutSeconds is < 1 or > 600
            || !double.IsFinite(Temperature) || Temperature is < 0 or > 2)
        {
            throw new ArgumentException("Configuration Chat invalide : vérifiez fenêtre, contexte, réserve et délai.");
        }
    }
}
