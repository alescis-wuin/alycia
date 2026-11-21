using System.Net.Http.Json;
using System.Text.Json;

namespace Alycia.Modules.Inference;

public sealed class OllamaEmbeddings(HttpClient http, InferenceScheduler scheduler) : ILocalEmbeddings
{
    public async Task<string> IdentityAsync(string model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200) throw new ArgumentException("Choisissez un modèle d’embeddings local dans Réglages.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await http.GetAsync("api/tags", timeout.Token); response.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        var local = list.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(m => m.GetProperty("name").GetString() == model
            && !model.Contains(":cloud", StringComparison.OrdinalIgnoreCase)
            && (!m.TryGetProperty("remote_host", out var host) || string.IsNullOrEmpty(host.GetString()))
            && (!m.TryGetProperty("remote_model", out var remote) || string.IsNullOrEmpty(remote.GetString())));
        if (local.ValueKind == JsonValueKind.Undefined || !local.TryGetProperty("digest", out var digest) || string.IsNullOrWhiteSpace(digest.GetString()))
            throw new ArgumentException("Modèle d’embeddings local avec empreinte introuvable.");
        using var show = await http.PostAsJsonAsync("api/show", new { model }, timeout.Token); show.EnsureSuccessStatusCode();
        using var details = JsonDocument.Parse(await show.Content.ReadAsStringAsync(timeout.Token));
        if ((details.RootElement.TryGetProperty("remote_host", out var remoteHost) && !string.IsNullOrEmpty(remoteHost.GetString()))
            || (details.RootElement.TryGetProperty("remote_model", out var remoteModel) && !string.IsNullOrEmpty(remoteModel.GetString())))
            throw new ArgumentException("Les embeddings distants ne sont pas autorisés.");
        if (!details.RootElement.GetProperty("capabilities").EnumerateArray().Any(c => c.GetString() == "embedding"))
            throw new ArgumentException("Ce modèle n’annonce pas la capacité embedding.");
        return model + ":" + digest.GetString();
    }
    public async Task<EmbeddingBatch> EmbedAsync(string model, IReadOnlyList<string> input, CancellationToken ct)
    {
        using var inference = await scheduler.AcquireAsync(TimeSpan.FromSeconds(180), ct);
        try
        {
            using var response = await http.PostAsJsonAsync("api/embed", new { model, input, truncate = false }, inference.Token);
            if (!response.IsSuccessStatusCode) throw new ChatModelException("Embeddings refusés : vérifiez le modèle local, sa fenêtre ou choisissez la recherche lexicale.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(inference.Token));
            var root = document.RootElement;
            var vectors = root.GetProperty("embeddings").Deserialize<float[][]>()!;
            if (vectors is null || vectors.Length != input.Count || vectors.Any(v => v is null || v.Length is < 1 or > 8192
                    || v.Any(x => !float.IsFinite(x))) || vectors.Any(v => v.Length != vectors[0].Length)) throw new ChatModelException("Vecteurs locaux invalides.");
            int? count = root.TryGetProperty("prompt_eval_count", out var p) ? p.GetInt32() : null;
            long? duration = root.TryGetProperty("total_duration", out var d) ? d.GetInt64() : null;
            return new(vectors, new(count, 0, duration, null, "embedding"));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatModelException("Le lot d’embeddings a dépassé 180 s. Réduisez le nombre de passages par lot dans les réglages d’indexation, puis relancez. Les lots terminés sont conservés.",
                failure: ChatModelFailure.Timeout);
        }
    }
}
