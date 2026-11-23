using System.Collections.Immutable;
using Alycia.Contracts.Workspaces;

namespace Alycia.Modules.Testing;

public sealed class MemoryProjectCatalog(IProjectNoteOrigins origins, Func<Guid> nextId, TimeProvider clock,
    Func<bool> inTransaction) : IProjectCatalog, IWorkspaceProfiles
{
    private readonly Dictionary<Guid, ProjectProfile> _profiles = [];
    private readonly Dictionary<Guid, string> _names = [];

    public Task<IReadOnlyList<ProjectEntry>> ListAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_profiles) return Task.FromResult<IReadOnlyList<ProjectEntry>>(_names
            .OrderBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Select(p => new ProjectEntry(p.Key, p.Value, _profiles[p.Key].Archived)).ToImmutableArray());
    }

    public Task<ProjectEntry> CreateAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = name?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 100 || normalized.Contains('\0')) throw new ArgumentException("Nom invalide.");
        lock (_profiles)
        {
            if (_names.Values.Contains(normalized, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Le nom existe.");
            var id = nextId();
            _names.Add(id, normalized);
            _profiles.Add(id, new(id, "", "", false, 0, []));
            return Task.FromResult(new ProjectEntry(id, normalized, false));
        }
    }

    public Task<ProjectProfile?> FindAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_profiles) return Task.FromResult(_profiles.GetValueOrDefault(id));
    }

    public Task<ProjectProfile> GetAsync(Guid id, bool allowArchived, CancellationToken ct) => RequireAsync(id, allowArchived, ct);

    public async Task<ProjectProfile> RequireAsync(Guid id, bool allowArchived, CancellationToken ct)
    {
        var value = await FindAsync(id, ct) ?? throw new ArgumentException("Projet introuvable.");
        if (!allowArchived && value.Archived) throw new ArgumentException("Projet archivé.");
        return value;
    }

    public async Task<string> ActiveDefaultModelAsync(Guid id, CancellationToken ct) => (await RequireAsync(id, false, ct)).DefaultModel;

    public async Task LockActiveDestinationAsync(Guid id, CancellationToken ct)
    {
        await RequireAsync(id, false, ct);
        if (!inTransaction()) throw new InvalidOperationException("Une transaction est requise.");
    }

    public async Task AdvanceAsync(Guid id, int expectedRevision, CancellationToken ct)
    {
        var current = await FindAsync(id, ct);
        if (current is null || current.Archived) throw new ChatConflictException();
        Save(current, expectedRevision);
    }

    public async Task<ProjectProfile> UpdateAsync(Guid id, int revision, string instructions, string defaultModel, bool archived, CancellationToken ct)
    {
        var current = await RequireAsync(id, true, ct);
        if (current.Revision != revision) throw new ChatConflictException();
        if (instructions is null || instructions.Length > 4000 || defaultModel is null || defaultModel.Length > 200
            || instructions.Contains('\0') || defaultModel.Contains('\0')) throw new ArgumentException("Consignes ou modèle invalides.");
        return Save(current with { Instructions = instructions.Trim(), DefaultModel = defaultModel.Trim(), Archived = archived }, revision);
    }

    public async Task<ProjectProfile> ChangeNoteAsync(Guid id, Guid? note, int revision, string text, Guid? conversation, int? sequence, bool remove, CancellationToken ct)
    {
        var current = await RequireAsync(id, false, ct);
        if (current.Revision != revision) throw new ChatConflictException();
        var notes = current.Notes.ToBuilder();
        var previous = notes.SingleOrDefault(n => n.Id == note);
        if (note.HasValue && previous is null || remove && previous is null) throw new ArgumentException("Note introuvable.");
        if (remove) notes.Remove(previous!);
        else
        {
            var value = text?.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 1500 || value.Contains('\0')) throw new ArgumentException("Note invalide.");
            await origins.ValidateAsync(id, conversation, sequence, ct);
            if (previous is null) notes.Add(new(nextId(), value, conversation, sequence, clock.GetUtcNow()));
            else notes[notes.IndexOf(previous)] = previous with { Text = value, UpdatedAtUtc = clock.GetUtcNow() };
            if (notes.Count > 50 || notes.Sum(n => n.Text.Length) > 8000) throw new ArgumentException("Budget des notes épuisé.");
        }
        return Save(current with { Notes = notes.ToImmutable() }, revision);
    }

    private ProjectProfile Save(ProjectProfile input, int expected)
    {
        lock (_profiles)
        {
            if (_profiles[input.Id].Revision != expected) throw new ChatConflictException();
            return _profiles[input.Id] = input with { Revision = expected + 1 };
        }
    }
}
