using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class AuthorizedToolSources(ChatDbContext database, ProjectSourceReader reader, IToolConversationPolicy policy, ILiveIndexFileAccess live, DocumentScope scope) : IAuthorizedToolSources
{
    public async Task<bool> HasFilesAsync(Guid conversation, CancellationToken ct)
    {
        var ids = await scope.ReadAsync(database, conversation, false, true, q => q.Where(f => f.ContextMode != "excluded" && f.Text != "")
            .Select(f => f.Id).ToArrayAsync(ct), ct);
        return ids.Length > 0 && (await live.CurrentEntriesAsync(conversation, ids, ct)).Any(f => f.Readable);
    }

    private async Task<IQueryable<Alycia.Modules.Chat.Domain.Attachment>> SourcesAsync(IQueryable<Alycia.Modules.Chat.Domain.Attachment> sources, Guid id, CancellationToken ct)
    {
        var owner = await policy.ReadAsync(id, ct) ?? throw new ConversationNotFoundException();
        if (owner.FirstVisibleSequence is not int first) return sources;
        return sources.Where(f => f.ContextMode != "excluded" && f.Text != "" && f.AnchorSequence <= owner.MessageCount + 1 &&
            (f.ContextMode == "pinned" || f.ContextMode == "project" || f.AnchorSequence >= first));
    }

    public async Task<ToolSourceEntry[]> EntriesAsync(Guid id, CancellationToken ct)
    {
        var files = await scope.ReadAsync(database, id, false, true, async q => await (await SourcesAsync(q, id, ct)).OrderBy(f => f.Name).ThenBy(f => f.Id)
            .Select(f => new ToolSourceEntry(f.Id, f.Name, f.Size, f.ContextMode, f.Status, f.ContextMode != "excluded" && f.Text != "")).ToArrayAsync(ct), ct);
        var current = (await live.CurrentEntriesAsync(id, files.Select(f => f.Id).ToArray(), ct)).ToDictionary(f => f.Id);
        return files.Where(f => current.ContainsKey(f.Id)).Select(f => f with
        {
            Readable = f.Readable && current[f.Id].Readable,
            Mode = current[f.Id].Mode == "excluded" ? "excluded" : f.Mode
        }).ToArray();
    }

    public async Task<AuthorizedToolSource> ReadAsync(Guid conversation, Guid fileId, CancellationToken ct)
    {
        var file = await scope.ReadAsync(database, conversation, false, true, async q => await (await SourcesAsync(q, conversation, ct)).SingleOrDefaultAsync(f => f.Id == fileId, ct), ct)
            ?? throw new ArgumentException("Fichier introuvable dans cette conversation.");
        var original = await reader.ReadAsync(file, ct);
        if (!await live.IsAllowedAsync(conversation, false, fileId, ct))
            throw new ArgumentException("Fichier introuvable dans cette conversation.");
        return new(file.Id, file.Name, file.Sha256, original.Text, original.Complete, original.Origin);
    }

    public IReadOnlyList<SourceChunk> Declarations(AuthorizedToolSource file, CancellationToken ct) =>
        CodeStructure.Split(file.Id, file.Name, file.Sha256, file.Text, ct);
}
