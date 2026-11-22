using System.Collections.Frozen;
using System.Text.Json;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Testing;

public sealed record ToolScenario(bool Enabled, bool HasSources,
    IReadOnlyDictionary<string, Func<JsonElement, int, CancellationToken, Task<ProjectToolResult>>> Commands);

public sealed class ScriptedProjectTools(IReadOnlyDictionary<Guid, ToolScenario> owners) : ISnapshotProjectTools, ISourceCommands
{
    private readonly FrozenDictionary<Guid, ToolScenario> _owners = owners.ToFrozenDictionary(p => p.Key,
        p => p.Value with { Commands = p.Value.Commands.ToFrozenDictionary(StringComparer.Ordinal) });

    public Task<bool> HasSourcesAsync(Guid owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_owners.TryGetValue(owner, out var state) && state.HasSources);
    }

    public async Task<ProjectToolResult> CallAsync(Guid owner, string name, JsonElement args, int maxResultBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_owners.TryGetValue(owner, out var state)) throw new ConversationNotFoundException();
        if (!state.Enabled) return Error(ToolRefusal.ForbiddenScope);
        try { return await ExecuteAsync(owner, name, args, maxResultBytes, ct); }
        catch (ArgumentException) { return Error(ToolRefusal.InvalidArguments); }
    }

    public Task<ProjectToolResult> ExecuteAsync(Guid owner, string name, JsonElement args, int bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ToolArguments.Validate(name, args, bytes);
        if (!_owners.TryGetValue(owner, out var state)) throw new ConversationNotFoundException();
        if (!state.Commands.TryGetValue(name, out var handler))
            throw new InvalidOperationException("Configurer la réponse de cet outil pour le scénario.");
        return handler(args.Clone(), bytes, ct);
    }

    public static ProjectToolResult Error(ToolRefusal reason) =>
        new(JsonSerializer.Serialize(new { error = reason.ToString() }), true) { Refusal = reason };
}
