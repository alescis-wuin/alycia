using Alicia.Domain.Conversations;

namespace Alicia.Application.Generations;

public interface IGenerationSnapshotStore
{
    Task<GenerationSnapshot?> FindAsync(
        GenerationSnapshotId snapshotId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        GenerationSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        GenerationSnapshotId snapshotId,
        CancellationToken cancellationToken = default);
}
