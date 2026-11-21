using System.Linq.Expressions;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class IndexSourceDocumentsAdapter(ChatDbContext database, DbContextOptions<ChatDbContext> options,
    ProjectSourceReader originals, ILiveIndexFileAccess live) : IIndexSourceDocuments
{
    private static readonly Expression<Func<Attachment, IndexSourceFile>> Metadata = f => new(f.Id, f.Name, f.Sha256, f.Status,
        f.ContextMode, f.CreatedAtUtc, f.SourceCreatedAt, f.SourceModifiedAt, f.MetadataSource);

    private static IQueryable<Attachment> Eligible(ChatDbContext context, Guid owner, bool project) =>
        (project ? context.Attachments.AsNoTracking().Where(f => f.WorkspaceId == owner) : context.ModelSources(owner))
        .Where(f => f.ContextMode != "excluded");

    public Task<IndexSourceFile[]> PageAsync(Guid owner, bool project, Guid[]? allowed, IndexFileCursor? after,
        bool orderByName, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        var query = Eligible(database, owner, project).Where(f => f.Text != "");
        if (allowed is not null)
        {
            var ids = allowed.ToArray();
            query = query.Where(f => ids.Contains(f.Id));
        }
        if (after is not null)
            query = orderByName ? query.Where(f => string.Compare(f.Name, after.Name) > 0 ||
                (f.Name == after.Name && f.Id.CompareTo(after.Id) > 0)) : query.Where(f => f.Id.CompareTo(after.Id) > 0);
        var ordered = orderByName ? query.OrderBy(f => f.Name).ThenBy(f => f.Id) : query.OrderBy(f => f.Id);
        return ordered.Take(count).Select(Metadata).ToArrayAsync(ct);
    }

    public Task<IndexSourceFile?> FindAsync(Guid owner, bool project, Guid file, CancellationToken ct) =>
        Eligible(database, owner, project).Where(f => f.Id == file).Select(Metadata).SingleOrDefaultAsync(ct);

    public async Task<IndexedOriginal> ReadAsync(Guid owner, bool project, Guid file, string sha256, CancellationToken ct)
    {
        // Independent readers may decode several documents concurrently.
        await using var current = new ChatDbContext(options);
        var source = await Eligible(current, owner, project).SingleOrDefaultAsync(f => f.Id == file && f.Sha256 == sha256, ct)
            ?? throw new ArgumentException("Cette source est indisponible.");
        var read = await originals.ReadAsync(source, ct);
        if (!await live.IsAllowedAsync(owner, project, file, ct)) throw new ArgumentException("Cette source est indisponible.");
        return new(read.Text, read.Complete, read.Origin);
    }

    public Task<Guid[]> ExcludedPageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        var query = database.Attachments.AsNoTracking().Where(f => f.ContextMode == "excluded");
        if (after.HasValue) query = query.Where(f => f.Id.CompareTo(after.Value) > 0);
        return query.OrderBy(f => f.Id).Select(f => f.Id).Take(count).ToArrayAsync(ct);
    }
}
