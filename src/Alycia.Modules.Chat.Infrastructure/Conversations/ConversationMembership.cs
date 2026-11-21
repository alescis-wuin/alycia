using Alycia.Contracts.Workspaces;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationMembership(ChatDbContext database, RelationalSession session,
    IWorkspaceProfiles profiles, IDocumentMembershipOverrides overrides, GenerationGate gate)
{
    public async Task<Conversation> SetMembershipAsync(Guid id, int revision, Guid? workspaceId, CancellationToken ct)
    {
        using var lease = gate.Enter(id);
        return await session.AtomicAsync(async token =>
        {
            var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, token) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, revision);
            if (workspaceId is Guid destination) await profiles.LockActiveDestinationAsync(destination, token);
            if (conversation.WorkspaceId != workspaceId)
            {
                await overrides.ClearAsync(id, token);
                conversation.AssignWorkspace(workspaceId);
                await database.SaveVersionedAsync(token);
            }
            return conversation;
        }, ct);
    }
}

public sealed class ProjectNoteOrigins(ChatDbContext database) : IProjectNoteOrigins
{
    public async Task ValidateAsync(Guid project, Guid? conversation, int? sequence, CancellationToken ct)
    {
        if (conversation is Guid origin && !await database.Database.SqlQuery<bool>($"""SELECT TRUE AS "Value" FROM chat.conversations WHERE "Id"={origin} AND "WorkspaceId"={project} FOR SHARE""").AnyAsync(ct))
            throw new ArgumentException("Cette conversation ne fait pas partie du projet.");
        if (sequence.HasValue && !await database.Database.SqlQuery<bool>($"""SELECT TRUE AS "Value" FROM chat.messages WHERE "ConversationId"={conversation} AND "Sequence"={sequence} FOR SHARE""").AnyAsync(ct))
            throw new ArgumentException("Message d’origine introuvable.");
    }
}
