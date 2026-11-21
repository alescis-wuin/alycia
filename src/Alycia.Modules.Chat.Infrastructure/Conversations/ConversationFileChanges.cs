using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationFileChanges(ChatDbContext database) : IConversationFileChanges
{
    public async Task<ConversationFileState> RequireAsync(Guid id, CancellationToken ct) =>
        await database.Conversations.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new ConversationFileState(c.Id, c.WorkspaceId, c.Revision, c.MessageCount)).SingleOrDefaultAsync(ct)
        ?? throw new ConversationNotFoundException();

    public async Task StageAdvanceAsync(Guid id, int expectedRevision, CancellationToken ct)
    {
        var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, expectedRevision);
        conversation.AttachFiles();
    }
}
