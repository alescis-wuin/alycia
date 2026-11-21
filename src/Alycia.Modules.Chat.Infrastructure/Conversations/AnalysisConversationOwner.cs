using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class AnalysisConversationOwner(ChatDbContext database) : IAnalysisConversation
{
    public async Task<AnalysisConversation> RequireAsync(Guid owner, int? revision, CancellationToken ct)
    {
        var state = await database.Conversations.AsNoTracking().Where(c => c.Id == owner)
            .Select(c => new AnalysisConversation(c.Id, c.Model, c.SettingsJson, c.Revision)).SingleOrDefaultAsync(ct)
            ?? throw new ConversationNotFoundException();
        if (revision.HasValue && revision != state.Revision) throw new ChatConflictException();
        return state;
    }

    public async Task<ConversationSummary> StageAdvanceAsync(Guid owner, int revision, CancellationToken ct)
    {
        var conversation = await database.Conversations.SingleOrDefaultAsync(c => c.Id == owner, ct)
            ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        conversation.AttachFiles();
        return ConversationProjection.Summary(conversation);
    }
}
