using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Context;

public sealed record SummarySource(string Key, string Text);
public interface ISummaryReducer
{
    int Calls { get; }
    Task<string> ReduceAsync(IReadOnlyList<SummarySource> sources, int targetBytes, CancellationToken ct);
}

public delegate ISummaryReducer CreateSummaryReducer(IChatModel model, string modelName, SummaryParameters settings,
    Func<string, Task>? progress = null, ModelCallBudget? sharedBudget = null,
    Func<string, CancellationToken, Task<string?>>? readNode = null, Func<string, string, CancellationToken, Task>? saveNode = null);
