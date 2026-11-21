using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class AuthorizedToolSources(ChatDbContext database, ProjectSourceReader reader, IToolConversationPolicy policy, ChatOptions defaults) : IAuthorizedToolSources
{
    public Task<bool> HasFilesAsync(Guid conversation, CancellationToken ct) => database.ModelSources(conversation)
        .AnyAsync(f => f.ContextMode != "excluded" && f.Text != "", ct);

    private async Task<IQueryable<Alycia.Modules.Chat.Domain.Attachment>> SourcesAsync(Guid id, CancellationToken ct)
    {
        var owner = await policy.ReadAsync(id, ct) ?? throw new ConversationNotFoundException();
        var options = ConversationConfiguration.Options(owner.SettingsJson, defaults);
        var sources = database.ModelSources(id);
        if (!options.AgentPlanningEnabled && options.RoutingMode == "off" && !options.VerificationSearchEnabled) return sources;
        var first = Math.Max(1, owner.MessageCount - 2 * (options.AutoSummarize ? options.ProtectedHistoryTurns : options.MaxHistoryTurns) + 1);
        return sources.Where(f => f.ContextMode != "excluded" && f.Text != "" && f.AnchorSequence <= owner.MessageCount + 1 &&
            (f.ContextMode == "pinned" || f.ContextMode == "project" || f.AnchorSequence >= first));
    }

    public async Task<ToolSourceEntry[]> EntriesAsync(Guid id, CancellationToken ct) => await (await SourcesAsync(id, ct)).OrderBy(f => f.Name).ThenBy(f => f.Id)
        .Select(f => new ToolSourceEntry(f.Id, f.Name, f.Size, f.ContextMode, f.Status, f.ContextMode != "excluded" && f.Text != "")).ToArrayAsync(ct);

    public async Task<AuthorizedToolSource> ReadAsync(Guid conversation, Guid fileId, CancellationToken ct)
    {
        var file = await (await SourcesAsync(conversation, ct)).SingleOrDefaultAsync(f => f.Id == fileId, ct)
            ?? throw new ArgumentException("Fichier introuvable dans cette conversation.");
        var original = await reader.ReadAsync(file, ct);
        return new(file.Id, file.Name, file.Sha256, original.Text, original.Complete, original.Origin);
    }

    public IReadOnlyList<SourceChunk> Declarations(AuthorizedToolSource file, CancellationToken ct) =>
        CodeStructure.Split(file.Id, file.Name, file.Sha256, file.Text, ct);
}
