using Alycia.Contracts.Chat;
using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Tools;

public interface ITurnTools
{
    Task<IProjectTools?> SelectAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct);
    Task<bool> AvailableForContextAsync(ToolTurnScope scope, bool enabled, IChatModel model, CancellationToken ct);
    Task<GenerationReply> GenerateAsync(IChatModel measured, IProjectTools? active, ToolTurnScope scope,
        ToolPrompt window, ToolGeneration settings, Func<ModelDelta, Task>? onDelta, CancellationToken ct);
}
