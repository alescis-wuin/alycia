namespace Alycia.Contracts.Indexing;

public interface IHistoryKnowledge
{
    Task<Dictionary<int, double>> HistoryAsync(Guid owner, string question, string profile, int through, DateTimeOffset before, CancellationToken ct);
}
