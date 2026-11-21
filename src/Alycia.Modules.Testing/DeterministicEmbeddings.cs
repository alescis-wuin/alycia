using System.Security.Cryptography;
using System.Text;

namespace Alycia.Modules.Testing;

public sealed class DeterministicEmbeddings : ILocalEmbeddings
{
    public Task<string> IdentityAsync(string model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult("mock-sha256-v1:" + model);
    }

    public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var vectors = new float[input.Count][];
        for (var i = 0; i < input.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            vectors[i] = SHA256.HashData(Encoding.UTF8.GetBytes(model + "\n" + input[i]))
                .Select(value => (value - 127.5f) / 127.5f).ToArray();
        }
        return Task.FromResult(new EmbeddingBatch(vectors, null));
    }
}
