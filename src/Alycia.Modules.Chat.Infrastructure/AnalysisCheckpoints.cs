using System.Text.Json;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed record AnalysisRun(Guid Id, Guid ConversationId, Guid? ScopeBatchId, string SourceFingerprint,
    string Identity, int Budget, string State, int CompletedUnits = 0, int ReusedUnits = 0,
    int FailedUnits = 0, Guid? AnalysisId = null, DateTimeOffset UpdatedAtUtc = default);
public sealed record AnalysisUnit(string Key, string Summary, bool Complete, string Origin, int Characters, int Utf8Bytes);

// Reconstructible local derivatives live beside originals and participate in existing filesystem backups.
public sealed class AnalysisCheckpoints(FileStorage storage)
{
    public const string PipelineVersion = "analysis-v1";
    private string DirectoryFor(Guid id) => Path.Combine(storage.DirectoryFor(id), PipelineVersion);
    public static string UnitKey(Guid file, string sha, string path, string status, string identity, int budget, AnalysisCacheProfile profile) =>
        profile.Key($"{PipelineVersion}|{StructuredDocuments.Version}|{CodeStructure.Version}|{file}|{sha}|{path}|{status}|{identity}|{budget}");
    public async Task<AnalysisRun[]> ListAsync(Guid conversation, CancellationToken ct)
    {
        var directory = DirectoryFor(conversation);
        if (!Directory.Exists(directory)) return [];
        var result = new List<AnalysisRun>();
        foreach (var path in Directory.EnumerateFiles(directory, "run-*.json").Take(50))
        {
            var run = await ReadAsync<AnalysisRun>(path, 65536, ct);
            if (run?.ConversationId == conversation) result.Add(run);
        }
        return result.OrderByDescending(r => r.UpdatedAtUtc).ToArray();
    }
    public async Task<AnalysisRun> StartAsync(Guid conversation, Guid? scope, string fingerprint, string identity, int budget, Guid? resume, CancellationToken ct)
    {
        AnalysisRun run;
        if (resume.HasValue)
        {
            run = await ReadAsync<AnalysisRun>(Path.Combine(DirectoryFor(conversation), "run-" + resume.Value.ToString("N") + ".json"), 65536, ct)
                ?? throw new ArgumentException("Point de reprise introuvable dans cette conversation.");
            if (run.ConversationId != conversation || run.SourceFingerprint != fingerprint || run.Identity != identity
                || run.ScopeBatchId != scope || run.Budget != budget || run.State == "completed")
                throw new ArgumentException("Reprise incompatible : démarrez une nouvelle analyse avec les paramètres actuels.");
        }
        else
        {
            if ((await ListAsync(conversation, ct)).Length >= 50) throw new ArgumentException("50 exécutions conservées ; reprenez une exécution existante.");
            run = new(Guid.NewGuid(), conversation, scope, fingerprint, identity, budget, "pending");
        }
        run = run with { State = "running", CompletedUnits = 0, ReusedUnits = 0, FailedUnits = 0 };
        await SaveRunAsync(run, ct); return run;
    }
    public Task SaveRunAsync(AnalysisRun run, CancellationToken ct) => WriteAsync(
        Path.Combine(DirectoryFor(run.ConversationId), "run-" + run.Id.ToString("N") + ".json"), run with { UpdatedAtUtc = DateTimeOffset.UtcNow }, ct);
    public void DeleteRun(Guid conversation, Guid run) => File.Delete(Path.Combine(DirectoryFor(conversation), "run-" + run.ToString("N") + ".json"));
    public async Task<AnalysisUnit?> ReadUnitAsync(Guid conversation, string key, CancellationToken ct)
    {
        ValidateKey(key);
        var unit = await ReadAsync<AnalysisUnit>(Path.Combine(DirectoryFor(conversation), key + ".json"), 65536, ct);
        return unit?.Key == key && !string.IsNullOrWhiteSpace(unit.Summary) ? unit : null;
    }
    public Task SaveUnitAsync(Guid conversation, AnalysisUnit unit, CancellationToken ct)
    {
        ValidateKey(unit.Key);
        if (string.IsNullOrWhiteSpace(unit.Summary) || unit.Summary.Length > 16384) throw new ArgumentException("Unité de résumé invalide.");
        return WriteAsync(Path.Combine(DirectoryFor(conversation), unit.Key + ".json"), unit, ct);
    }
    private static void ValidateKey(string key)
    {
        if (key.Length != 64 || key.Any(c => !char.IsAsciiHexDigit(c))) throw new ArgumentException("Clé de dérivé invalide.");
    }
    private static async Task<T?> ReadAsync<T>(string path, long maxBytes, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > maxBytes) return default;
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), ProjectToolCatalog.Json); }
        catch (JsonException) { return default; }
    }
    private static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, ProjectToolCatalog.Json), ct);
            ct.ThrowIfCancellationRequested(); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
