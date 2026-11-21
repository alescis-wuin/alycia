using Alycia.Modules.Chat.Domain;
using System.Text.Json.Serialization;

namespace Alycia.Modules.Chat.Application;

public interface IChatTurnScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}

public interface IChatRepository
{
    Task<IChatTurnScope?> BeginTurnAsync(Guid id, CancellationToken ct) => Task.FromResult<IChatTurnScope?>(null);
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
public sealed record ContextWindow(IReadOnlyList<PromptMessage> Messages, int HistoryMessagesIncluded, int EstimatedInputTokens, int FilesIncluded = 0, bool NeedsSummary = false, int SummaryThroughSequence = 0, string SummaryText = "", int SummaryCalls = 0, IReadOnlyList<SourceExcerpt>? Sources = null, int FilesAvailable = 0, int ToolReserve = 0, RetrievalReport? Retrieval = null, MemoryReport? Memory = null, QueryRoute? Route = null, EvidenceAssembly? Assembly = null, TemporalReport? Temporal = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? MemoryTexts = null, UsedGuide[]? Guides = null);
public sealed record GenerationReply(string Content, GenerationStats? Stats = null, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? Thinking = null, IReadOnlyList<ToolObservation>? Tools = null, IReadOnlyList<string>? Warnings = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? EvidenceTexts = null, ResearchReport? Research = null);
public sealed record SendMessageResult(IReadOnlyList<ChatMessage> Messages, int? HistoryMessagesIncluded,
    bool Replayed, int FilesIncluded = 0, int? EstimatedInputTokens = null, GenerationStats? Stats = null, GenerationUsage? Usage = null);

public interface IChatModel
{
    Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions options, CancellationToken ct) => Task.FromResult(options.Copy());
    async Task<GenerationReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions options, CancellationToken ct) => new(await GenerateAsync(model, messages, options, ct));
    Task<long?> ResidentVramAsync(string model, CancellationToken ct) => Task.FromResult<long?>(null);
    Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => Task.FromResult(false);
    Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages,
        ChatOptions settings, Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        throw new ChatModelException("Ce fournisseur ne prend pas en charge les outils.");
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);
    Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken cancellationToken);
    // Providers enforce settings.TimeoutSeconds after acquiring their inference slot; ct cancels queueing too.
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
public enum ChatModelFailure { Other, EmptyContent, Timeout }
public sealed class ChatModelException(string message, GenerationStats? stats = null, ChatModelFailure failure = ChatModelFailure.Other) : Exception(message)
{
    public GenerationStats? Stats { get; } = stats;
    public ChatModelFailure Failure { get; } = failure;
}
