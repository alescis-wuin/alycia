namespace Alycia.Contracts.Memory;

public sealed record TemporalOwner(Guid Id, int Through, string LedgerJson);
public sealed record TemporalMessage(int Sequence, string Role, string Content, DateTimeOffset CreatedAtUtc);

public interface ITemporalHistorySources
{
    Task<TemporalOwner[]> OwnersAsync(Guid? after, int count, CancellationToken ct);
    Task<int[]> SequencesAsync(Guid owner, int after, int through, CancellationToken ct);
    Task<TemporalMessage[]> MessagesAsync(Guid owner, int[] sequences, CancellationToken ct);
}

public interface ITemporalFactStorage
{
    Task<int> ContiguousThroughAsync(Guid owner, int through, CancellationToken ct);
    Task<int[]> IndexedAsync(Guid owner, int[] sequences, CancellationToken ct);
    Task SaveAsync(Guid owner, int sequence, DateTimeOffset observed, string[] topics, string facts, string search, CancellationToken ct);
    Task<int> CountAsync(Guid owner, int through, CancellationToken ct);
    Task<List<string>> LexicalAsync(Guid owner, int through, DateTimeOffset before, string query, CancellationToken ct);
    Task<List<string>> SelectedAsync(Guid owner, int through, DateTimeOffset before, int[] sequences, CancellationToken ct);
    Task<List<string>> TopicsAsync(Guid owner, int through, DateTimeOffset before, string[] topics, CancellationToken ct);
    Task<List<string>> ExportAsync(Guid owner, int after, int limit, CancellationToken ct);
    Task<int> ClearAsync(Guid owner, CancellationToken ct);
}
