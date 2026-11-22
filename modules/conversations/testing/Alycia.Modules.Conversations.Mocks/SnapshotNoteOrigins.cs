using System.Collections.Immutable;
using Alycia.Contracts.Workspaces;

namespace Alycia.Modules.Testing;

public sealed class SnapshotNoteOrigins(IEnumerable<(Guid Project, Guid Conversation, int Count)> conversations) : IProjectNoteOrigins
{
    private readonly Dictionary<Guid, (Guid Project, int Count)> _owners =
        conversations.ToDictionary(r => r.Conversation, r => (r.Project, r.Count));

    public Task ValidateAsync(Guid project, Guid? conversation, int? sequence, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (conversation is null && sequence is null) return Task.CompletedTask;
        if (conversation is not Guid id || !_owners.TryGetValue(id, out var owner) || owner.Project != project
            || sequence.HasValue && (sequence < 1 || sequence > owner.Count)) throw new ArgumentException("Provenance invalide.");
        return Task.CompletedTask;
    }
}
