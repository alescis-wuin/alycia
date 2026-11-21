using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Indexing;
using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Workspaces.Infrastructure;

public sealed class WorkspaceProfileStore(RelationalSession session, IWorkspaceIdentityDirectory identities)
    : IWorkspaceProfiles, IProjectIndexOwners
{
    public Task<ProjectProfile?> FindAsync(Guid id, CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT "Instructions", "DefaultModel", "Archived", "Revision", "NotesJson"
                FROM chat.workspace_profiles WHERE "Id" = @id
                """;
            RelationalSession.AddParameter(command, "id", id);
            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) return (ProjectProfile?)null;
            var notes = JsonSerializer.Deserialize<ProjectNote[]>(reader.GetString(4)) ?? [];
            return new ProjectProfile(id, reader.GetString(0), reader.GetString(1), reader.GetBoolean(2),
                reader.GetInt32(3), notes.ToImmutableArray());
        }, ct);

    public async Task<ProjectProfile> RequireAsync(Guid id, bool allowArchived, CancellationToken ct)
    {
        var profile = await FindAsync(id, ct);
        if (profile is null)
        {
            if (!await identities.ExistsAsync(id, ct)) throw new ArgumentException("Projet introuvable.");
            await InsertAsync(id, ct);
            profile = await FindAsync(id, ct) ?? throw new ChatConflictException();
        }
        if (profile.Archived && !allowArchived) throw new ArgumentException("Restaurez d’abord ce projet archivé.");
        return profile;
    }

    public async Task LockActiveDestinationAsync(Guid id, CancellationToken ct)
    {
        await RequireAsync(id, false, ct);
        await session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            if (transaction is null) throw new InvalidOperationException("La validation de destination exige une transaction.");
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """SELECT NOT "Archived" FROM chat.workspace_profiles WHERE "Id"=@id FOR SHARE""";
            RelationalSession.AddParameter(command, "id", id);
            if (await command.ExecuteScalarAsync(token) is not true)
                throw new ArgumentException("Restaurez d’abord ce projet archivé.");
            return true;
        }, ct);
    }

    internal Task InsertAsync(Guid id, CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO chat.workspace_profiles ("Id", "Instructions", "DefaultModel", "NotesJson", "Archived", "Revision")
                VALUES (@id, '', '', '[]', FALSE, 0) ON CONFLICT ("Id") DO NOTHING
                """;
            RelationalSession.AddParameter(command, "id", id);
            try { return await command.ExecuteNonQueryAsync(token); }
            catch (DbException failure) { throw new DbUpdateException("Échec de création du profil.", failure); }
        }, ct);

    internal Task<HashSet<Guid>> ArchivedAsync(CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """SELECT "Id" FROM chat.workspace_profiles WHERE "Archived" """;
            await using var reader = await command.ExecuteReaderAsync(token);
            var ids = new HashSet<Guid>();
            while (await reader.ReadAsync(token)) ids.Add(reader.GetGuid(0));
            return ids;
        }, ct);

    public Task<ProjectIndexOwner[]> ActivePageAsync(Guid? after, int count, CancellationToken ct)
    {
        if (count is < 1 or > 512) throw new ArgumentOutOfRangeException(nameof(count));
        return session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT "Id", "Revision" FROM chat.workspace_profiles
                WHERE NOT "Archived" AND (@after::uuid IS NULL OR "Id" > @after) ORDER BY "Id" LIMIT @count
                """;
            RelationalSession.AddParameter(command, "after", (object?)after ?? DBNull.Value);
            RelationalSession.AddParameter(command, "count", count);
            await using var reader = await command.ExecuteReaderAsync(token);
            var page = new List<ProjectIndexOwner>();
            while (await reader.ReadAsync(token)) page.Add(new(reader.GetGuid(0), reader.GetInt32(1)));
            return page.ToArray();
        }, ct);
    }

    internal Task<ProjectProfile> SaveAsync(ProjectProfile next, CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE chat.workspace_profiles SET "Instructions"=@instructions, "DefaultModel"=@model,
                    "NotesJson"=@notes, "Archived"=@archived, "Revision"="Revision"+1
                WHERE "Id"=@id AND "Revision"=@revision
                """;
            RelationalSession.AddParameter(command, "instructions", next.Instructions);
            RelationalSession.AddParameter(command, "model", next.DefaultModel);
            RelationalSession.AddParameter(command, "notes", JsonSerializer.Serialize(next.Notes));
            RelationalSession.AddParameter(command, "archived", next.Archived);
            RelationalSession.AddParameter(command, "revision", next.Revision);
            RelationalSession.AddParameter(command, "id", next.Id);
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new ChatConflictException();
            return next with { Revision = next.Revision + 1 };
        }, ct);

    public Task AdvanceAsync(Guid id, int expectedRevision, CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE chat.workspace_profiles SET "Revision"="Revision"+1
                WHERE "Id"=@id AND "Revision"=@revision AND NOT "Archived"
                """;
            RelationalSession.AddParameter(command, "id", id);
            RelationalSession.AddParameter(command, "revision", expectedRevision);
            if (await command.ExecuteNonQueryAsync(token) != 1) throw new ChatConflictException();
            return true;
        }, ct);
}
