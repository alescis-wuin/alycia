using Alycia.Modules.Workspaces.Domain;
using Alycia.Modules.Workspaces.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Workspaces.Tests;

public sealed class WorkspaceMappingTests
{
    [Fact]
    public void Name_uniqueness_and_case_insensitivity_are_enforced_in_the_database_mapping()
    {
        var options = new DbContextOptionsBuilder<WorkspacesDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=test");
        using var database = new WorkspacesDbContext(options.Options);
        var mapping = database.Model.FindEntityType(typeof(Workspace))!;
        Assert.Equal("workspaces", mapping.GetSchema());
        Assert.Equal("citext", mapping.FindProperty(nameof(Workspace.Name))!.GetColumnType());
        Assert.Contains(mapping.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(Workspace.Name));
    }
}
