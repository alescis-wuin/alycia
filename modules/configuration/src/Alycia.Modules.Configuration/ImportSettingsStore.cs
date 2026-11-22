using Npgsql;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Configuration;

public sealed class ImportSettingsStore(DbContext database) : IImportLimitsReader
{
    public async Task<ImportLimits> LoadAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(database.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT settings_json FROM chat.import_settings WHERE id = 1", connection);
        var json = await command.ExecuteScalarAsync(ct) as string;
        var limits = json is null ? new ImportLimits() : JsonSerializer.Deserialize<ImportLimits>(json, ProjectToolCatalog.Json)!;
        limits.Validate(); return limits;
    }
    public async Task SaveAsync(ImportLimits limits, CancellationToken ct)
    {
        limits.Validate();
        await using var connection = new NpgsqlConnection(database.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("INSERT INTO chat.import_settings(id, settings_json) VALUES (1, $1) ON CONFLICT(id) DO UPDATE SET settings_json = EXCLUDED.settings_json", connection);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(limits, ProjectToolCatalog.Json));
        await command.ExecuteNonQueryAsync(ct);
    }
}
