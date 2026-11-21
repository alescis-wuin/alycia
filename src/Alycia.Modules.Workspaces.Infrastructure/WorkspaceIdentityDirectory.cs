using System.Data.Common;
using Alycia.Contracts.Workspaces;
using Alycia.Modules.Workspaces.Domain;
using Alycia.Platform.Persistence;

namespace Alycia.Modules.Workspaces.Infrastructure;

public sealed class WorkspaceIdentityDirectory(RelationalSession session) : IWorkspaceIdentityDirectory
{
    public Task<IReadOnlyList<WorkspaceIdentity>> ListAsync(CancellationToken ct) =>
        session.WithConnectionAsync<IReadOnlyList<WorkspaceIdentity>>(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id, name::text FROM workspaces.workspaces ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(token);
            var result = new List<WorkspaceIdentity>();
            while (await reader.ReadAsync(token)) result.Add(new(reader.GetGuid(0), reader.GetString(1)));
            return result;
        }, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
        session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT EXISTS (SELECT 1 FROM workspaces.workspaces WHERE id = @id)";
            AddParameter(command, "id", id);
            return (bool)(await command.ExecuteScalarAsync(token))!;
        }, ct);

    public Task<WorkspaceIdentity> CreateAsync(string name, CancellationToken ct)
    {
        var workspace = Workspace.Create(name);
        return session.WithConnectionAsync(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO workspaces.workspaces (id, name, created_at_utc) VALUES (@id, @name, @created)
                ON CONFLICT (name) DO NOTHING
                """;
            AddParameter(command, "id", workspace.Id);
            AddParameter(command, "name", workspace.Name);
            AddParameter(command, "created", workspace.CreatedAtUtc);
            if (await command.ExecuteNonQueryAsync(token) == 0) throw new ArgumentException("Ce nom de projet existe déjà.");
            return new WorkspaceIdentity(workspace.Id, workspace.Name);
        }, ct);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
