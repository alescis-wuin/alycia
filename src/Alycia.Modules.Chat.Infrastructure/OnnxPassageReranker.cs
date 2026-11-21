using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Alycia.Modules.Chat.Application;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Tokenizers.HuggingFace.Tokenizer;

namespace Alycia.Modules.Chat.Infrastructure;

// The model directory is operator configuration and never comes from a conversation.
public sealed class OnnxPassageReranker(string? directory) : IPassageReranker, IDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);
    private InferenceSession? _model;
    private Tokenizer? _tokenizer;
    private string _identity = "";

    public async Task<RerankScores> ScoreAsync(string question, IReadOnlyList<SourceChunk> passages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(directory) || !await _slot.WaitAsync(0, ct))
            throw new InvalidOperationException("Reclassement local indisponible.");
        // Keep ownership of the native session until the native call has actually returned.
        var work = Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                EnsureLoaded();
                using var run = new RunOptions();
                using var cancellation = ct.Register(() => run.Terminate = true);
                var scores = new double[passages.Count];
                for (var n = 0; n < passages.Count; n++)
                {
                    ct.ThrowIfCancellationRequested();
                    var passage = passages[n];
                    var pair = _tokenizer!.Encode(question, true, passage.Path + "\n" + passage.Symbol + "\n" + passage.Text,
                        includeTypeIds: true, includeAttentionMask: true).First();
                    if (pair.Ids.Count() is < 1 or > 384) throw new InvalidDataException("Paire de tokens invalide.");
                    var inputs = _model!.InputMetadata.Keys.Select(name =>
                    {
                        var values = name switch
                        {
                            "input_ids" => pair.Ids,
                            "attention_mask" => pair.AttentionMask,
                            "token_type_ids" => pair.TypeIds,
                            _ => throw new InvalidDataException("Entrée ONNX inconnue.")
                        };
                        var tokens = values.Select(x => (long)x).ToArray();
                        return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(tokens, new[] { 1, tokens.Length }));
                    }).ToArray();
                    using var output = _model.Run(inputs, new[] { "logits" }, run);
                    scores[n] = output.First().AsTensor<float>().Single();
                }
                return new RerankScores(_identity, scores);
            }
            finally { _slot.Release(); }
        });
        return await work.WaitAsync(ct);
    }

    private void EnsureLoaded()
    {
        if (_model is not null) return;
        var path = Path.Combine(directory!, "onnx", "model_quantized.onnx");
        var tokenizerPath = Path.Combine(directory!, "tokenizer.json");
        var bytes = File.ReadAllBytes(tokenizerPath);
        var config = JsonNode.Parse(bytes)!.AsObject();
        config["truncation"] = JsonNode.Parse("""{"direction":"Right","max_length":384,"strategy":"LongestFirst","stride":0}""");
        config["padding"] = null;
        var temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, config.ToJsonString());
            using var weights = File.OpenRead(path);
            _identity = $"onnx-cpu-v1:{Convert.ToHexString(SHA256.HashData(weights))}:{Convert.ToHexString(SHA256.HashData(bytes))}";
            using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
            var tokenizer = Tokenizer.FromFile(temp);
            try { _model = new InferenceSession(path, options); _tokenizer = tokenizer; }
            catch { tokenizer.Dispose(); throw; }
        }
        finally { File.Delete(temp); }
    }
    public void Dispose()
    {
        _slot.Wait();
        try { _tokenizer?.Dispose(); _model?.Dispose(); }
        finally { _slot.Release(); }
    }
}
