using System.Collections.Immutable;
using System.Text.Json;

namespace Alycia.Modules.Inference;

internal static class PromptSnapshot
{
    public static ImmutableArray<PromptMessage> Capture(IReadOnlyList<PromptMessage> messages) =>
        messages.Select(m => m.ToolCalls is null ? m : m with
        {
            ToolCalls = m.ToolCalls.Select(call => call with
            {
                Function = call.Function with { Arguments = Copy(call.Function.Arguments) }
            }).ToImmutableArray()
        }).ToImmutableArray();

    private static JsonElement Copy(JsonElement value) =>
        value.ValueKind == JsonValueKind.Undefined ? default : value.Clone();
}
