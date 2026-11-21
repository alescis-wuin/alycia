using System.Linq.Expressions;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Infrastructure;

public static class DocumentInventory
{
    public static readonly Expression<Func<Attachment, InventoryFile>> Projection = f => new InventoryFile(
        f.Id, f.BatchId, f.ArchiveId, f.Name, f.Size, f.Sha256, Path.GetExtension(f.Name), f.SourceCreatedAt,
        f.SourceModifiedAt, f.MetadataSource, f.CreatedAtUtc, f.Status, f.ContextMode);

    private static readonly Func<Attachment, InventoryFile> Project = Projection.Compile();

    public static InventoryFile[] Describe(IEnumerable<Attachment> files) => files.Select(Project)
        .OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Id).ToArray();
}
