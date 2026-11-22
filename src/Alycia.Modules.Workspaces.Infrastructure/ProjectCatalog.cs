using System.Collections.Immutable;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Workspaces;
using Alycia.Platform.Persistence;

namespace Alycia.Modules.Workspaces.Infrastructure;

public sealed class ProjectCatalog(RelationalSession session, IWorkspaceIdentityDirectory identities,
    WorkspaceProfileStore profiles, IProjectNoteOrigins origins) : IProjectCatalog
{
    public async Task<IReadOnlyList<ProjectEntry>> ListAsync(CancellationToken ct)
    {
        var names = await identities.ListAsync(ct);
        var archived = await profiles.ArchivedAsync(ct);
        return names.Select(n => new ProjectEntry(n.Id, n.Name, archived.Contains(n.Id))).ToArray();
    }

    public Task<ProjectEntry> CreateAsync(string name, CancellationToken ct) =>
        session.AtomicAsync(async token =>
        {
            var workspace = await identities.CreateAsync(name, token);
            await profiles.InsertAsync(workspace.Id, token);
            return new ProjectEntry(workspace.Id, workspace.Name, false);
        }, ct);

    public Task<ProjectProfile> GetAsync(Guid id, bool allowArchived, CancellationToken ct) =>
        profiles.RequireAsync(id, allowArchived, ct);

    public async Task<string> ActiveDefaultModelAsync(Guid id, CancellationToken ct) =>
        (await GetAsync(id, false, ct)).DefaultModel;

    public async Task<ProjectProfile> UpdateAsync(Guid id, int revision, string instructions, string defaultModel, bool archived, CancellationToken ct)
    {
        var profile = await GetAsync(id, true, ct);
        if (profile.Revision != revision) throw new ChatConflictException();
        if (instructions is null || instructions.Length > 4000 || defaultModel is null ||
            defaultModel.Length > 200 || instructions.Contains('\0') || defaultModel.Contains('\0'))
            throw new ArgumentException("Consignes ou modèle par défaut invalides.");
        return await profiles.SaveAsync(profile with { Instructions = instructions.Trim(), DefaultModel = defaultModel.Trim(), Archived = archived }, ct);
    }

    public Task<ProjectProfile> ChangeNoteAsync(Guid id, Guid? note, int revision, string text, Guid? originConversationId, int? originSequence, bool remove, CancellationToken ct) =>
        session.AtomicAsync(async token =>
        {
            var profile = await GetAsync(id, false, token);
            if (profile.Revision != revision) throw new ChatConflictException();
            var notes = profile.Notes.ToList();
            var index = notes.FindIndex(n => n.Id == note);
            if (note.HasValue && index < 0) throw new ArgumentException("Note introuvable.");
            if (remove) notes.RemoveAt(index);
            else
            {
                var normalized = text?.Trim();
                if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 1500 || normalized.Contains('\0'))
                    throw new ArgumentException("La note doit contenir entre 1 et 1500 caractères, sans caractère nul.");
                await origins.ValidateAsync(id, originConversationId, originSequence, token);
                if (index >= 0) notes[index] = notes[index] with { Text = normalized, UpdatedAtUtc = DateTimeOffset.UtcNow };
                else notes.Add(new(Guid.NewGuid(), normalized, originConversationId, originSequence, DateTimeOffset.UtcNow));
                if (notes.Count > 50 || notes.Sum(n => n.Text.Length) > 8000) throw new ArgumentException("Notes du projet limitées à 50 notes et 8 000 caractères.");
            }
            return await profiles.SaveAsync(profile with { Notes = notes.ToImmutableArray() }, token);
        }, ct);
}
