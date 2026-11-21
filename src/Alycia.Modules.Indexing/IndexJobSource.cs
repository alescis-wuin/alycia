using System.Globalization;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Workspaces;

namespace Alycia.Modules.Indexing;

public sealed class IndexJobSource(IConversationIndexOwners conversations, IWorkspaceProfiles profiles)
{
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
        return FormattableString.Invariant($"{chat.Revision}:{(workspace is { Archived: false } ? workspace.Revision : 0)}");
    }
}
