using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Inference;

public sealed record EmbeddingBatch(float[][] Vectors, GenerationStats? Stats);
public interface ILocalEmbeddings
{
    Task<string> IdentityAsync(string model, CancellationToken ct);
    Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct);
}
