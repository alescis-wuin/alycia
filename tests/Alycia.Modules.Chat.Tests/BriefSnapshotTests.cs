using Alycia.Contracts.Retrieval;
using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class BriefSnapshotTests
{
    [Fact]
    public async Task Brief_fallback_preserves_the_submitted_arrays_while_inspecting_the_model()
    {
        var gate = new TaskCompletionSource<ModelTokenization>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refiner = new QueryBriefRefiner(new PendingInspection(gate.Task));
        string[] labels = ["initial"];
        string[] constraints = ["locale"];
        var brief = new QueryBrief("question", labels, [], [], [], null, "question", constraints);
        var pending = refiner.RefineAsync(brief, new("fixture", 4096, 256, 10), default);
        Assert.False(pending.IsCompleted);
        labels[0] = "modifié";
        constraints[0] = "autre";
        gate.SetResult(new(null));
        var result = await pending;
        Assert.Equal(["initial"], result.Labels);
        Assert.Equal(["locale"], Assert.IsType<string[]>(result.Constraints));
    }

    private sealed class PendingInspection(Task<ModelTokenization> inspection) : IChatModel
    {
        public Task<ModelTokenization> InspectAsync(string model, string tokenCounting, CancellationToken ct) => inspection.WaitAsync(ct);
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => throw new InvalidOperationException();
        public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) => throw new InvalidOperationException();
    }
}
