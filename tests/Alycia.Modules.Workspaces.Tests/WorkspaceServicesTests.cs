using Alycia.Modules.Workspaces.Application;
using Alycia.Modules.Workspaces.Domain;

namespace Alycia.Modules.Workspaces.Tests;

public sealed class WorkspaceServicesTests
{
    [Fact]
    public async Task CreateAsync_RejectsCaseInsensitiveTrimmedDuplicate()
    {
        var repository = new FakeRepository();
        var service = new CreateWorkspaceService(repository);

        await service.ExecuteAsync("Personal");
        await Assert.ThrowsAsync<WorkspaceNameConflictException>(() =>
            service.ExecuteAsync(" personal "));

        Assert.Equal(1, repository.Count);
    }

    [Fact]
    public async Task CreateAsync_DoesNotWriteInvalidWorkspace()
    {
        var repository = new FakeRepository();
        var service = new CreateWorkspaceService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync("  "));

        Assert.Equal(0, repository.Count);
    }

    [Fact]
    public async Task GetAsync_ReturnsPersistedWorkspaceOrNull()
    {
        var repository = new FakeRepository();
        var created = await new CreateWorkspaceService(repository).ExecuteAsync(" Personal ");
        var service = new GetWorkspaceService(repository);

        Assert.Same(created, await service.ExecuteAsync(created.Id));
        Assert.Null(await service.ExecuteAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(Guid.Empty));
    }

    private sealed class FakeRepository : IWorkspaceRepository
    {
        private readonly List<Workspace> _items = [];

        public int Count => _items.Count;

        public Task<bool> ExistsByNameAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.Exists(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<Workspace?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_items.Find(item => item.Id == id));
        }

        public Task AddAsync(
            Workspace workspace,
            CancellationToken cancellationToken = default)
        {
            _items.Add(workspace);
            return Task.CompletedTask;
        }
    }
}
