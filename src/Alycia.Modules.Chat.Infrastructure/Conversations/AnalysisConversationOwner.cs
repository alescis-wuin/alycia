using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class AnalysisConversationOwner(DbContext database, ChatOptions defaults, IChatModel model) : IAnalysisConversation
{
    public async Task<AnalysisConversation> RequireAsync(Guid owner, int? revision, CancellationToken ct)
    {
        var state = await database.Set<Conversation>().AsNoTracking().Where(c => c.Id == owner)
            .Select(c => new AnalysisConversation(c.Id, c.Model, c.Revision)).SingleOrDefaultAsync(ct)
            ?? throw new ConversationNotFoundException();
        if (revision.HasValue && revision != state.Revision) throw new ChatConflictException();
        return state;
    }

    public async Task<AnalysisPreferences> PreferencesAsync(Guid owner, int revision, CancellationToken ct)
    {
        var fallback = defaults.Copy();
        var state = await database.Set<Conversation>().AsNoTracking().Where(c => c.Id == owner)
            .Select(c => new { c.SettingsJson, c.Revision, c.Model }).SingleOrDefaultAsync(ct) ?? throw new ConversationNotFoundException();
        if (state.Revision != revision) throw new ChatConflictException();
        var options = await model.PrepareOptionsAsync(state.Model, StoredConversationSettings.Read(state.SettingsJson, () => fallback), ct);
        return new(options.Summary(), options.Enrichment.Enabled ? options.Enrichment.Workers : 1,
            new AnalysisCacheProfile(System.Text.Json.JsonSerializer.Serialize(options, ProjectToolCatalog.Json)));
    }

    public async Task<ConversationSummary> StageAdvanceAsync(Guid owner, int revision, CancellationToken ct)
    {
        var conversation = await database.Set<Conversation>().SingleOrDefaultAsync(c => c.Id == owner, ct)
            ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        conversation.AttachFiles();
        return ConversationProjection.Summary(conversation);
    }
}
