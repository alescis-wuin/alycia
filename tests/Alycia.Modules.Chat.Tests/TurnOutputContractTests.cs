using Alycia.Contracts.Context;
using Alycia.Contracts.Inference;
using Alycia.Contracts.Memory;
using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Tools;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Retrieval;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Chat.Tests;

public sealed class TurnOutputContractTests
{
    [Fact]
    public async Task Context_tools_and_audit_preserve_the_prompt_stream_refusals_and_cancellation()
    {
        var owner = Guid.NewGuid(); var request = Guid.NewGuid();
        var model = new ScriptedTextGeneration(["bon", "jour"]); var meter = new MemoryModelMeter(model);
        var options = new ChatOptions { AutoSummarize = false, MaxHistoryTurns = 2, UseProjectTools = false };
        var c = new ContextConversation(owner, 2, ScriptedTextGeneration.Model, "système", "", 0, "", "", "{}");
        var reader = new SnapshotTurnReads([owner], [new(owner, 1, request, "user", "avant", default), new(owner, 2, request, "assistant", "réponse", default)], new SnapshotTurnDocuments([]));
        var scope = new ToolTurnScope(owner, null, c.Model); var parameters = options.Context();
        foreach (ITurnContext context in new ITurnContext[] { new TurnContext(new ContextMemory(), reader, new Alycia.Modules.Context.SlidingHistory()), new FixtureTurnContext(reader, new ReferenceSlidingHistory()) })
        {
            var budget = await context.AllocateAsync(c, "demande", parameters, false, default);
            Assert.Equal(0, budget.ToolReserve);
            var reserve = await context.AllocateAsync(c, "demande", parameters, true, default);
            Assert.True(reserve.ToolReserve >= 0); Assert.True(reserve.Options.AvailableInputTokens <= budget.Options.AvailableInputTokens);
            var built = await context.BuildAsync(c, "demande", budget, model, meter, TurnContextMode.Preview, null, default);
            Assert.Equal(new[] { "système", "avant", "réponse", "demande" }, built.Window.Messages.Select(m => m.Content));
            Assert.Equal(2, built.Window.HistoryMessagesIncluded); Assert.Null(built.Prepared);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.AllocateAsync(c, "", parameters, false, new(true)));
        }
        var window = new ContextWindow([new("user", "demande")], 0, 1);
        foreach (ITurnTools tools in new ITurnTools[] { new TurnTools(RecursiveSummarizer.Create), new FixtureTurnTools() })
        {
            Assert.Null(await tools.SelectAsync(scope, true, model, default));
            Assert.False(await tools.AvailableForContextAsync(scope, false, model, default));
            var events = new List<string>();
            var reply = await tools.GenerateAsync(model, null, scope, window.ToolPrompt(), options.ToolGeneration(), d => { events.Add(d.Content); return Task.CompletedTask; }, default);
            Assert.Equal(new[] { "bon", "jour" }, events);
            Assert.Equal(reply.Content, string.Concat(events));
            Assert.Equal(reply.Content, (await tools.GenerateAsync(model, null, scope, window.ToolPrompt(), options.ToolGeneration(), null, default)).Content);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.SelectAsync(scope, false, model, new(true)));
        }
        const string known = "S123456789ABC"; const string other = "SFFFFFFFFFFFF";
        var supplied = window with { Sources = [new(owner, "document", 5, false, "initial", known, 1, 1, "sha", 0, 5)] };
        foreach (ITurnResponseAudit audit in new ITurnResponseAudit[] { new TurnResponseAudit(), new FixtureResponseAudit() })
        {
            var received = await audit.ReviewAsync(owner, new($"Texte [{known}] [{other}]."), supplied, new(false, 1, null), meter, null, default);
            Assert.Equal("unknown_references", received.Citations.Status);
            Assert.Equal(known, Assert.Single(received.Citations.Recognized)); Assert.Equal(other, Assert.Single(received.Citations.Unknown));
            Assert.Equal(owner, Assert.Single(received.Citations.Supplied).FileId);
            await Assert.ThrowsAsync<ChatModelException>(() => audit.ReviewAsync(owner, new(" "), window, new(false, 1, null), meter, null, default));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => audit.ReviewAsync(owner, new("ok"), window, new(false, 1, null), meter, null, new(true)));
        }
    }
}
