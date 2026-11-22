using System.Security.Cryptography;
using System.Text;

namespace Alycia.Modules.Testing;

public sealed class DeterministicEmbeddings(IReadOnlyDictionary<string, string>? identities = null, Func<string, string, float[]>? vectorFor = null) : ILocalEmbeddings
{
    private readonly Dictionary<string, string>? _identities = identities?.ToDictionary();

    public Task<string> IdentityAsync(string model, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200) throw new ArgumentException("Nom de modèle invalide.");
        if (_identities is null) return Task.FromResult("mock-sha256-v1:" + model);
        return _identities.TryGetValue(model, out var digest) ? Task.FromResult(model + ":" + digest)
            : throw new ArgumentException("Modèle simulé indisponible.");
    }

    public Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var vectors = new float[input.Count][];
        for (var i = 0; i < input.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            vectors[i] = vectorFor?.Invoke(model, input[i]).ToArray() ?? SHA256.HashData(Encoding.UTF8.GetBytes(model + "\n" + input[i]))
                .Select(value => (value - 127.5f) / 127.5f).ToArray();
        }
        return Task.FromResult(new EmbeddingBatch(vectors, null));
    }
}
