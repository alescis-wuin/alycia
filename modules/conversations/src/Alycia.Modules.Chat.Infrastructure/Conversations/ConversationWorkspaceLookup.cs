using System.Data;
using Alycia.Platform.Persistence;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ConversationWorkspaceLookup(RelationalSession session) : IConversationWorkspaceLookup
{
    public Task<Guid?> FindWorkspaceAsync(Guid conversation, CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            if (transaction is null) throw new InvalidOperationException("La lecture documentaire exige un instantané.");
            await using var query = connection.CreateCommand();
            query.Transaction = transaction;
            if (transaction.IsolationLevel is IsolationLevel.RepeatableRead or IsolationLevel.Serializable)
                query.CommandText = """SELECT "WorkspaceId" FROM chat.conversations WHERE "Id"=@owner""";
            else
                query.CommandText = """SELECT "WorkspaceId" FROM chat.conversations WHERE "Id"=@owner FOR SHARE""";
            RelationalSession.AddParameter(query, "owner", conversation);
            return await query.ExecuteScalarAsync(token) is Guid workspace ? (Guid?)workspace : null;
        }, ct);
}
