namespace Alycia.Modules.Chat.Application;

// All keys in one lookup belong to the caller's eligible index snapshot.
public interface IEmbeddingSimilarityReader
{
    Task<Dictionary<string, double>> SimilaritiesAsync(string identity, string[] keys, float[] query, CancellationToken ct);
    Task<Dictionary<int, double>> MemorySimilaritiesAsync(Guid owner, string identity, int through, DateTimeOffset before,
        float[] query, CancellationToken ct);
}
