using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class PromptCatalog(ChatDbContext database)
{
    public async Task<IReadOnlyList<PromptProfile>> ListAsync(CancellationToken ct) =>
        await database.PromptProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<PromptProfile> CreateAsync(string? name, string? systemPrompt, CancellationToken ct)
    {
        var profile = new PromptProfile
        {
            Id = Guid.NewGuid(),
            Name = Conversation.RequiredText(name, 100, "Le nom"),
            SystemPrompt = Conversation.RequiredText(systemPrompt, 4000, "Le prompt")
        };
        if (await database.PromptProfiles.CountAsync(ct) >= 200) throw new ArgumentException("Bibliothèque limitée à 200 profils.");
        database.PromptProfiles.Add(profile);
        await database.SaveChangesAsync(ct);
        return profile;
    }

    public async Task<PromptProfile?> UpdateAsync(Guid id, string? name, string? systemPrompt, CancellationToken ct)
    {
        var profile = await database.PromptProfiles.FindAsync([id], ct);
        if (profile is null) return null;
        profile.Name = Conversation.RequiredText(name, 100, "Le nom");
        profile.SystemPrompt = Conversation.RequiredText(systemPrompt, 4000, "Le prompt");
        await database.SaveChangesAsync(ct);
        return profile;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct) =>
        await database.PromptProfiles.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
}
