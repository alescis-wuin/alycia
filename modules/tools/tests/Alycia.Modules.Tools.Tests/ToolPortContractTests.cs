using System.Text.Json;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Documents;
using Alycia.Contracts.Tools;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Tools.Tests;

public sealed class ToolPortContractTests
{
    [Fact]
    public async Task Tools_scope_pages_and_refusals_survive_contract_and_snapshot_dispatch()
    {
        var owner = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        var file = Guid.NewGuid();
        var entries = new SnapshotToolSources([new(owner, false, new(file, "alpha.txt", 5, "window", "ready", true),
            new(file, "alpha.txt", "hash", "alpha", true, "original"), [])]);
        var policy = new SnapshotConversations([new(owner, null, 0, null, 0), new(disabled, null, 0, null, 0)], [], new Dictionary<Guid, ToolConversationPolicy>
        {
            [owner] = new(true, 0, null, null),
            [disabled] = new(false, 0, null, null)
        });
        var commands = new SourceCommands(entries);
        var implementation = new ProjectToolService(new FixturePassageScoring(), entries, commands, policy,
            new SnapshotTurnReads([owner, disabled], [], new SnapshotTurnDocuments([])), new SnapshotIndex(owner, []));
        var handlers = new Dictionary<string, Func<JsonElement, int, CancellationToken, Task<ProjectToolResult>>>
        {
            ["project_list"] = (args, _, _) =>
            {
                var offset = ToolArguments.Int(args, "offset", 0, 0, int.MaxValue);
                return Task.FromResult(new ProjectToolResult(JsonSerializer.Serialize(new
                {
                    total = 1,
                    files = new[] { new { fileId = file } }.Skip(offset)
                })));
            },
            ["project_read"] = (args, _, _) =>
            {
                var id = args.GetProperty("fileId").GetGuid();
                return Task.FromResult(id == file ? new ProjectToolResult(JsonSerializer.Serialize(new { text = "alpha" }))
                    : ScriptedProjectTools.Error(ToolRefusal.UnavailableSource));
            }
        };
        var fixture = new ScriptedProjectTools(new Dictionary<Guid, ToolScenario>
        {
            [owner] = new(true, true, handlers),
            [disabled] = new(false, false, handlers)
        });
        handlers.Clear();
        foreach (var endpoint in new ISnapshotProjectTools[] { implementation, fixture })
        {
            IProjectTools ordinary = endpoint;
            Assert.True(await ordinary.HasSourcesAsync(owner, default));
            Assert.False(await ordinary.HasSourcesAsync(disabled, default));
            Assert.Equal(ToolRefusal.ForbiddenScope, (await endpoint.CallAsync(disabled, "project_list", JsonSerializer.SerializeToElement(new { }), 4096, default)).Refusal);
            var foreign = await ordinary.CallAsync(owner, "project_read", JsonSerializer.SerializeToElement(new { fileId = Guid.Empty }), 4096, default);
            Assert.True(foreign.IsError);
            Assert.Equal(ToolRefusal.UnavailableSource, foreign.Refusal);
            var invalid = await ordinary.CallAsync(owner, "unknown", JsonSerializer.SerializeToElement(new { }), 4096, default);
            Assert.True(invalid.IsError);
            Assert.Equal(ToolRefusal.InvalidArguments, invalid.Refusal);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ordinary.CallAsync(owner, "project_list", JsonSerializer.SerializeToElement(new { }), 4096, new(true)));
        }
        foreach (var executor in new ISourceCommands[] { commands, fixture })
        {
            foreach (var offset in new[] { 0, 1 })
            {
                var result = await executor.ExecuteAsync(owner, "project_list", JsonSerializer.SerializeToElement(new { offset }), 4096, default);
                Assert.False(result.IsError);
                using var json = JsonDocument.Parse(result.Text);
                Assert.Equal(1, json.RootElement.GetProperty("total").GetInt32());
                Assert.Equal(1 - offset, json.RootElement.GetProperty("files").GetArrayLength());
                if (offset == 0) Assert.Equal(file, json.RootElement.GetProperty("files")[0].GetProperty("fileId").GetGuid());
            }
            using var bad = JsonDocument.Parse("""{"offset":0,"offset":9}""");
            await Assert.ThrowsAsync<ArgumentException>(() => executor.ExecuteAsync(owner, "project_list", bad.RootElement, 4096, default));
            var refused = await executor.ExecuteAsync(owner, "project_read", JsonSerializer.SerializeToElement(new { fileId = Guid.Empty }), 4096, default);
            Assert.Equal(ToolRefusal.UnavailableSource, refused.Refusal);
        }
    }
}
