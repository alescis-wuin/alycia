using System.Text.Json;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Infrastructure;

public sealed class ProjectCatalog(ChatDbContext db) : IProjectCatalog
{
    private sealed record ProjectName(Guid Id, string Name);

    public async Task<IReadOnlyList<ProjectEntry>> ListAsync(CancellationToken ct)
    {
        var names = await db.Database.SqlQuery<ProjectName>($"""SELECT id AS "Id", name::text AS "Name" FROM workspaces.workspaces ORDER BY name""").ToArrayAsync(ct);
        var profiles = await db.WorkspaceProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        return names.Select(n => new ProjectEntry(n.Id, n.Name, profiles.GetValueOrDefault(n.Id)?.Archived ?? false)).ToArray();
    }

    public async Task<ProjectEntry> CreateAsync(string name, CancellationToken ct)
    {
        var workspace = Workspace.Create(name);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO workspaces.workspaces (id, name, created_at_utc) VALUES ({workspace.Id}, {workspace.Name}, {workspace.CreatedAtUtc})
            ON CONFLICT (name) DO NOTHING
            """, ct);
        if (rows == 0) throw new ArgumentException("Ce nom de projet existe déjà.");
        db.WorkspaceProfiles.Add(new() { Id = workspace.Id });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(workspace.Id, workspace.Name, false);
    }

    public async Task<ProjectProfile> GetAsync(Guid id, bool allowArchived, CancellationToken ct) =>
        Profile(await ProjectProfiles.RequireAsync(db, id, ct, allowArchived));

    public async Task<string> ActiveDefaultModelAsync(Guid id, CancellationToken ct) =>
        (await ProjectProfiles.RequireAsync(db, id, ct)).DefaultModel;

    public async Task<ProjectProfile> UpdateAsync(Guid id, int revision, string instructions, string defaultModel, bool archived, CancellationToken ct)
    {
        var project = await ProjectProfiles.RequireAsync(db, id, ct, true);
        ProjectProfiles.CheckRevision(project, revision);
        if (instructions is null || instructions.Length > 4000 || defaultModel is null
            || defaultModel.Length > 200 || instructions.Contains('\0') || defaultModel.Contains('\0'))
            throw new ArgumentException("Consignes ou modèle par défaut invalides.");
        project.Instructions = instructions.Trim();
        project.DefaultModel = defaultModel.Trim();
        project.Archived = archived;
        project.Revision++;
        await new EfChatRepository(db).SaveAsync(ct);
        return Profile(project);
    }

    public async Task<Conversation> SetMembershipAsync(Guid id, int revision, Guid? workspaceId, CancellationToken ct)
    {
        using var gate = GenerationGate.Shared.Enter(id);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var conversation = await db.Conversations.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ConversationNotFoundException();
        ConversationConfiguration.CheckRevision(conversation, revision);
        if (workspaceId is Guid destination) await ProjectProfiles.RequireAsync(db, destination, ct);
        if (conversation.WorkspaceId != workspaceId)
        {
            await db.WorkspaceFileOverrides.Where(o => o.ConversationId == id).ExecuteDeleteAsync(ct);
            conversation.AssignWorkspace(workspaceId);
            await new EfChatRepository(db).SaveAsync(ct);
        }
        await tx.CommitAsync(ct);
        return conversation;
    }

    public async Task<ProjectProfile> ChangeNoteAsync(Guid id, Guid? note, int revision, string text, Guid? originConversationId, int? originSequence, bool remove, CancellationToken ct)
    {
        var profile = await ProjectProfiles.RequireAsync(db, id, ct);
        ProjectProfiles.CheckRevision(profile, revision);
        var notes = profile.ReadNotes().ToList();
        int index = notes.FindIndex(n => n.Id == note);
        if (note.HasValue && index < 0) throw new ArgumentException("Note introuvable.");
        if (remove) notes.RemoveAt(index);
        else
        {
            var normalized = Conversation.RequiredText(text, 1500, "La note");
            if (originConversationId is Guid origin && !await db.Conversations.AnyAsync(c => c.Id == origin && c.WorkspaceId == id, ct))
                throw new ArgumentException("Cette conversation ne fait pas partie du projet.");
            if (originSequence.HasValue && !await db.Messages.AnyAsync(m => m.ConversationId == originConversationId && m.Sequence == originSequence, ct))
                throw new ArgumentException("Message d’origine introuvable.");
            if (index >= 0) notes[index] = notes[index] with { Text = normalized, UpdatedAtUtc = DateTimeOffset.UtcNow };
            else notes.Add(new(Guid.NewGuid(), normalized, originConversationId, originSequence, DateTimeOffset.UtcNow));
            if (notes.Count > 50 || notes.Sum(n => n.Text.Length) > 8000) throw new ArgumentException("Notes du projet limitées à 50 notes et 8 000 caractères.");
        }
        profile.NotesJson = JsonSerializer.Serialize(notes);
        profile.Revision++;
        await new EfChatRepository(db).SaveAsync(ct);
        return Profile(profile);
    }

    private static ProjectProfile Profile(WorkspaceProfile profile) =>
        new(profile.Id, profile.Instructions, profile.DefaultModel, profile.Archived, profile.Revision, profile.ReadNotes());
}
