using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public static class ProjectProfiles
{
    public static void CheckRevision(WorkspaceProfile profile, int revision)
    {
        if (profile.Revision != revision) throw new ChatConflictException();
    }

    public static async Task<WorkspaceProfile> RequireAsync(ChatDbContext db, Guid id, CancellationToken ct, bool archived = false)
    {
        var profile = await db.WorkspaceProfiles.FindAsync([id], ct);
        if (profile is null)
        {
            // Projects created by the original Workspaces API acquire chat metadata on first use.
            var exists = await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM workspaces.workspaces WHERE id = {id}) AS \"Value\"").SingleAsync(ct);
            if (!exists) throw new ArgumentException("Projet introuvable.");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO chat.workspace_profiles ("Id", "Instructions", "DefaultModel", "NotesJson", "Archived", "Revision")
                VALUES ({id}, '', '', '[]', FALSE, 0) ON CONFLICT ("Id") DO NOTHING
                """, ct);
            profile = await db.WorkspaceProfiles.SingleAsync(p => p.Id == id, ct);
        }
        if (profile.Archived && !archived) throw new ArgumentException("Restaurez d’abord ce projet archivé.");
        return profile;
    }
}
