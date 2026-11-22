using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationFileChanges(DbContext database) : IConversationFileChanges
{
    public async Task<ConversationFileState> RequireAsync(Guid id, CancellationToken ct) =>
        await database.Set<Conversation>().AsNoTracking().Where(c => c.Id == id)
            .Select(c => new ConversationFileState(c.Id, c.WorkspaceId, c.Revision, c.MessageCount)).SingleOrDefaultAsync(ct)
        ?? throw new ConversationNotFoundException();

    public async Task StageAdvanceAsync(Guid id, int expectedRevision, CancellationToken ct)
    {
        var conversation = await database.Set<Conversation>().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, expectedRevision);
        conversation.AttachFiles();
    }
}
