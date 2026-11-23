using Alycia.Modules.Workspaces.Application;
using Alycia.Modules.Workspaces.Domain;
using Alycia.Modules.Workspaces.Tests.Doubles;

namespace Alycia.Modules.Workspaces.Tests;

public sealed class WorkspaceServicesTests
{
    [Fact]
    public async Task CreateAsync_RejectsCaseInsensitiveTrimmedDuplicate()
    {
        var repository = new MemoryWorkspaceRepository();
        var service = new CreateWorkspaceService(repository);

        await service.ExecuteAsync("Personal");
        await Assert.ThrowsAsync<WorkspaceNameConflictException>(() =>
            service.ExecuteAsync(" personal "));

        Assert.Equal(1, repository.Count);
    }

    [Fact]
    public async Task CreateAsync_DoesNotWriteInvalidWorkspace()
    {
        var repository = new MemoryWorkspaceRepository();
        var service = new CreateWorkspaceService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync("  "));

        Assert.Equal(0, repository.Count);
    }

    [Fact]
    public async Task GetAsync_ReturnsPersistedWorkspaceOrNull()
    {
        var repository = new MemoryWorkspaceRepository();
        var created = await new CreateWorkspaceService(repository).ExecuteAsync(" Personal ");
        var service = new GetWorkspaceService(repository);

        Assert.Same(created, await service.ExecuteAsync(created.Id));
        Assert.Null(await service.ExecuteAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(Guid.Empty));
    }
}
