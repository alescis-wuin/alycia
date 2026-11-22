using System.Text.Json;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Tools.Tests;

public sealed class ScopeTests
{
    [Fact]
    public async Task Source_commands_reject_duplicate_keys_unknown_tools_and_an_underfunded_response()
    {
        var owner = Guid.NewGuid();
        var commands = new SourceCommands(new SnapshotToolSources([]));
        async Task Reject(string tool, string json, int budget)
        {
            using var document = JsonDocument.Parse(json);
            await Assert.ThrowsAsync<ArgumentException>(() => commands.ExecuteAsync(owner, tool, document.RootElement, budget, default));
        }
        await Reject("project_list", """{"offset":1,"offset":2}""", 1024);
        await Reject("project_list", "{}", 1023);
        await Reject("project_private", "{}", 2048);
        using var args = JsonDocument.Parse("{}");
        var result = await commands.ExecuteAsync(owner, "project_list", args.RootElement, 2048, default);
        Assert.False(result.IsError);
        using var parsed = JsonDocument.Parse(result.Text);
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
    }
}
