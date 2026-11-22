using System.Globalization;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Workspaces;

namespace Alycia.Modules.Indexing;

public sealed class IndexJobSource(IConversationIndexOwners conversations, IWorkspaceProfiles profiles,
    Func<Guid, bool, IReadOnlySet<(Guid Id, string Hash)>, CancellationToken, Task<HashSet<(Guid Id, string Hash)>>> eligibleFiles)
{
    public Task<HashSet<(Guid Id, string Hash)>> EligibleFilesAsync(Guid owner, bool project,
        IReadOnlySet<(Guid Id, string Hash)> hashes, CancellationToken ct) => eligibleFiles(owner, project, hashes, ct);

    public async Task<bool> HistoryMatchesAsync(Guid owner, int sequence, SourceChunk chunk, CancellationToken ct)
    {
        var page = await conversations.HistoryPageAsync(owner, sequence - 1, sequence, 1, ct);
        return page is [var message] && message.RequestId == chunk.FileId && message.Role == chunk.Symbol
            && chunk.Path == $"{owner:N}/history/{sequence}" && chunk.Sha256 == MemoryTransfer.Hash(message.Content)
            && chunk.End <= message.Content.Length && message.Content.AsSpan(chunk.Start, chunk.End - chunk.Start).SequenceEqual(chunk.Text);
    }

    public async Task<string?> HistoryHashAsync(Guid owner, int sequence, CancellationToken ct)
    {
        var page = await conversations.HistoryPageAsync(owner, sequence - 1, sequence, 1, ct);
        return page is [var message] ? MemoryTransfer.Hash(message.Content) : null;
    }

    public async Task<string?> RevisionAsync(Guid owner, bool project, CancellationToken ct)
    {
        if (project)
        {
            var source = await profiles.FindAsync(owner, ct);
            return source is { Archived: false } ? source.Revision.ToString(CultureInfo.InvariantCulture) : null;
        }
        var chat = await conversations.FindAsync(owner, ct);
        if (chat is null) return null;
        var workspace = chat.WorkspaceId is Guid id ? await profiles.FindAsync(id, ct) : null;
        if (chat.WorkspaceId.HasValue && workspace is not { Archived: false }) return null;
        return FormattableString.Invariant($"{chat.Revision}:{(workspace is { Archived: false } ? workspace.Revision : 0)}");
    }
}
