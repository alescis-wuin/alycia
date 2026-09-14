using System.Text.Json;
using System.Text.Json.Serialization;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed class LlamaCppObservabilityLog
{
    internal const string FileName = "generation-observations.jsonl";
    internal const string PreviousFileName = "generation-observations.previous.jsonl";
    internal const long MaxFileBytes = 1024L * 1024L;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _gate = new();
    private readonly string _logDirectory;

    public LlamaCppObservabilityLog(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        _logDirectory = Path.GetFullPath(logDirectory);
    }

    public void TryAppend(InferenceProviderGenerationObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_logDirectory);
                string path = Path.Combine(_logDirectory, FileName);
                RotateIfNeeded(path);
                string line = JsonSerializer.Serialize(observation, _jsonOptions);
                File.AppendAllText(path, string.Concat(line, Environment.NewLine));
            }
        }
        catch (IOException)
        {
            // Observability is best-effort and must never fail or alter generation.
        }
        catch (UnauthorizedAccessException)
        {
            // Observability is best-effort and must never fail or alter generation.
        }
    }

    private void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxFileBytes)
        {
            return;
        }

        string previousPath = Path.Combine(_logDirectory, PreviousFileName);

        if (File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }

        File.Move(path, previousPath);
    }
}
