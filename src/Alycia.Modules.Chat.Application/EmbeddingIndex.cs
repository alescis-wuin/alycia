using Alycia.Contracts.Context;
namespace Alycia.Modules.Chat.Application;

public sealed record MemoryVectorMatches(IReadOnlyDictionary<int, double> Scores, IReadOnlyList<ModelCallUsage> Calls, string? Warning = null);
public interface IIndexedEmbeddings
{
    Task<SemanticSearch> SearchAsync(Guid conversation, string query, ChatOptions settings,
        IReadOnlyList<SourceChunk> chunks, CancellationToken ct);
    Task<MemoryVectorMatches> SearchMemoryAsync(Guid owner, string question, ChatOptions settings, int through, DateTimeOffset before, CancellationToken ct) =>
        Task.FromResult(new MemoryVectorMatches(new Dictionary<int, double>(), [], "Index vectoriel historique indisponible."));
    Task<SemanticSearch> SearchHistoryAsync(Guid conversation, string query, ChatOptions settings,
        IReadOnlyList<HistoryMessage> messages, CancellationToken ct) =>
        Task.FromResult(new SemanticSearch(new double[messages.Count], 0, []));
}
