using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Configuration;

public sealed class PromptCatalog(DbContext database)
{
    public async Task<IReadOnlyList<PromptProfile>> ListAsync(CancellationToken ct) =>
        await database.Set<PromptProfile>().AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<PromptProfile> CreateAsync(string? name, string? systemPrompt, CancellationToken ct)
    {
        var profile = new PromptProfile
        {
            Id = Guid.NewGuid(),
            Name = SettingsInput.RequiredText(name, 100, "Le nom"),
            SystemPrompt = SettingsInput.RequiredText(systemPrompt, 4000, "Le prompt")
        };
        if (await database.Set<PromptProfile>().CountAsync(ct) >= 200) throw new ArgumentException("Bibliothèque limitée à 200 profils.");
        database.Set<PromptProfile>().Add(profile);
        await database.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<PromptProfile?> UpdateAsync(Guid id, string? name, string? systemPrompt, CancellationToken ct)
    {
        var profile = await database.Set<PromptProfile>().FindAsync([id], ct);
        if (profile is null) return null;
        profile.Name = SettingsInput.RequiredText(name, 100, "Le nom");
        profile.SystemPrompt = SettingsInput.RequiredText(systemPrompt, 4000, "Le prompt");
        await database.SaveChangesAsync(ct);
        return profile;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        await database.Set<PromptProfile>().Where(p => p.Id == id).ExecuteDeleteAsync(ct);
}
