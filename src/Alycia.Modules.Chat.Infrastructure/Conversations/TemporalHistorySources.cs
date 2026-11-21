using Alycia.Contracts.Memory;
using Alycia.Modules.Chat.Application;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class TemporalHistorySources(ChatDbContext database, ChatOptions defaults) : ITemporalHistorySources
{
    public async Task<TemporalOwner[]> OwnersAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        // Filter settings after paging stored JSON. Advance past disabled pages as well.
        var result = new List<TemporalOwner>();
        while (result.Count < count)
        {
            var size = count - result.Count;
            var rows = await database.Conversations.AsNoTracking()
                .Where(c => c.MessageCount > 0 && (!after.HasValue || c.Id.CompareTo(after.Value) > 0))
                .OrderBy(c => c.Id).Take(size).Select(c => new { c.Id, c.MessageCount, c.SettingsJson, c.MemoryLedgerJson }).ToArrayAsync(ct);
            result.AddRange(rows.Where(c => ConversationConfiguration.Options(c.SettingsJson, defaults).TemporalMemoryEnabled)
                .Select(c => new TemporalOwner(c.Id, c.MessageCount, c.MemoryLedgerJson)));
            if (rows.Length < size) break;
            after = rows[^1].Id;
        }
        return result.ToArray();
    }

    public Task<int[]> SequencesAsync(Guid owner, int after, int through, CancellationToken ct) =>
        database.Messages.AsNoTracking().Where(m => m.ConversationId == owner && m.Sequence > after && m.Sequence <= through)
            .OrderBy(m => m.Sequence).Select(m => m.Sequence).Take(256).ToArrayAsync(ct);

    public Task<TemporalMessage[]> MessagesAsync(Guid owner, int[] sequences, CancellationToken ct)
    {
        if (sequences.Length > 32) throw new ArgumentOutOfRangeException(nameof(sequences));
        var ids = sequences.ToArray();
        return database.Messages.AsNoTracking().Where(m => m.ConversationId == owner && ids.Contains(m.Sequence))
            .OrderBy(m => m.Sequence).Select(m => new TemporalMessage(m.Sequence, m.Role, m.Content, m.CreatedAtUtc)).ToArrayAsync(ct);
    }
}
