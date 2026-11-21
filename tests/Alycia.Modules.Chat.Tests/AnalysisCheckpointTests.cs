using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class AnalysisCheckpointTests
{
    [Fact]
    public async Task RestartRetainsValidatedUnitsAndRejectsChangedIdentityScopeAndCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = new FileStorage(root); var id = Guid.NewGuid(); var file = Guid.NewGuid();
        try
        {
            var store = new AnalysisCheckpoints(storage);
            var run = await store.StartAsync(id, null, "source", "model:digest", 1024, null, default);
            var profile = new Alycia.Contracts.Documents.AnalysisCacheProfile(
                System.Text.Json.JsonSerializer.Serialize(new ChatOptions(), ProjectToolCatalog.Json));
            var key = AnalysisCheckpoints.UnitKey(file, "sha", "code.cs", "complete", "model:digest", 1024, profile);
            await store.SaveUnitAsync(id, new(key, "résumé validé", true, "original", 100, 100), default);
            await store.SaveRunAsync(run with { State = "cancelled", CompletedUnits = 1 }, default);
            store = new AnalysisCheckpoints(storage);
            Assert.Equal("cancelled", Assert.Single(await store.ListAsync(id, default)).State);
            Assert.NotNull(await store.ReadUnitAsync(id, key, default));
            Assert.Null(await store.ReadUnitAsync(Guid.NewGuid(), key, default));
            Assert.Equal(run.Id, (await store.StartAsync(id, null, "source", "model:digest", 1024, run.Id, default)).Id);
            await Assert.ThrowsAsync<ArgumentException>(() => store.StartAsync(id, null, "changed", "model:digest", 1024, run.Id, default));
            await Assert.ThrowsAsync<ArgumentException>(() => store.StartAsync(id, null, "source", "model:new", 1024, run.Id, default));
            Assert.NotEqual(key, AnalysisCheckpoints.UnitKey(file, "newsha", "code.cs", "complete", "model:digest", 1024, profile));
            var path = Path.Combine(storage.DirectoryFor(id), AnalysisCheckpoints.PipelineVersion, key + ".json");
            await File.WriteAllTextAsync(path, "broken");
            Assert.Null(await store.ReadUnitAsync(id, key, default));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveUnitAsync(id, new(key, "new", true, "original", 1, 1), cancelled.Token));
            Assert.Equal("broken", await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
