using System.Data.Common;
using Alycia.Platform.Persistence;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationIndexOwners(RelationalSession session) : IConversationIndexOwners
{
    private static ConversationIndexOwner Read(DbDataReader rows)
    {
        var stored = rows.GetString(3);
        var policy = stored == "{}" ? null : IndexParameters.Capture(StoredConversationSettings.Read(stored, () => new ChatSettings()));
        return new(rows.GetGuid(0), rows.IsDBNull(1) ? null : rows.GetGuid(1), rows.GetInt32(2), policy, rows.GetInt32(4));
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
        session.WithConnectionAsync(async (db, tx, token) =>
        {
            await using var query = db.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """SELECT EXISTS(SELECT 1 FROM chat.conversations WHERE "Id"=@id)""";
            RelationalSession.AddParameter(query, "id", id);
            return (bool)(await query.ExecuteScalarAsync(token))!;
        }, ct);

    public Task<ConversationIndexOwner?> FindAsync(Guid id, CancellationToken ct) =>
        session.WithConnectionAsync(async (db, tx, token) =>
        {
            await using var query = db.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """SELECT "Id", "WorkspaceId", "Revision", "SettingsJson", "MessageCount" FROM chat.conversations WHERE "Id"=@id""";
            RelationalSession.AddParameter(query, "id", id);
            await using var rows = await query.ExecuteReaderAsync(token);
            return await rows.ReadAsync(token) ? Read(rows) : null;
        }, ct);

    public Task<ConversationIndexOwner[]> PageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(count));
        return session.WithConnectionAsync(async (db, tx, token) =>
        {
            await using var query = db.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """
                SELECT "Id", "WorkspaceId", "Revision", "SettingsJson", "MessageCount" FROM chat.conversations
                WHERE @after::uuid IS NULL OR "Id" > @after ORDER BY "Id" LIMIT @count
                """;
            RelationalSession.AddParameter(query, "after", after);
            RelationalSession.AddParameter(query, "count", count);
            await using var rows = await query.ExecuteReaderAsync(token);
            var result = new List<ConversationIndexOwner>();
            while (await rows.ReadAsync(token)) result.Add(Read(rows));
            return result.ToArray();
        }, ct);
    }

    public Task<IndexHistoryMessage[]> HistoryPageAsync(Guid owner, int after, int through, int count, CancellationToken ct)
    {
        if (count is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(count));
        return session.WithConnectionAsync(async (db, tx, token) =>
        {
            await using var query = db.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """
                SELECT "RequestId", "Sequence", "Role", "Content", "CreatedAtUtc" FROM chat.messages
                WHERE "ConversationId"=@owner AND "Sequence">@after AND "Sequence"<=@through ORDER BY "Sequence" LIMIT @count
                """;
            RelationalSession.AddParameter(query, "owner", owner);
            RelationalSession.AddParameter(query, "after", after);
            RelationalSession.AddParameter(query, "through", through);
            RelationalSession.AddParameter(query, "count", count);
            await using var rows = await query.ExecuteReaderAsync(token);
            var result = new List<IndexHistoryMessage>();
            while (await rows.ReadAsync(token))
                result.Add(new(owner, rows.GetGuid(0), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetFieldValue<DateTimeOffset>(4)));
            return result.ToArray();
        }, ct);
    }
}
