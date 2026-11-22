using Alycia.Contracts.Workspaces;

namespace Alycia.Modules.Testing;

public sealed class SnapshotProjectDefaults(IEnumerable<ProjectProfile> profiles, IEnumerable<Guid>? identities = null) : IProjectModelDefaults
{
    private readonly Dictionary<Guid, ProjectProfile> _profiles = profiles.ToDictionary(p => p.Id);

    private readonly HashSet<Guid> _known = (identities ?? []).ToHashSet();

    public Task<string> ActiveDefaultModelAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_profiles.TryGetValue(id, out var profile))
            return _known.Contains(id) ? Task.FromResult("") : throw new ArgumentException("Projet introuvable.");
        if (profile.Archived) throw new ArgumentException("Restaurez d’abord ce projet archivé.");
        return Task.FromResult(profile.DefaultModel);
    }
}
