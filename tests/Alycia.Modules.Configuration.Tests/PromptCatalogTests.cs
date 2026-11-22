using Alycia.Modules.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Configuration.Tests;

public sealed class PromptCatalogTests
{
    [Theory]
    [InlineData(" ", "instruction")]
    [InlineData("nom", " ")]
    public async Task Invalid_prompts_are_rejected_before_any_persistent_write(string name, string prompt)
    {
        await using var database = new DbContext(new DbContextOptionsBuilder().UseNpgsql("Host=127.0.0.1;Port=1;Database=test").Options);
        var catalog = new PromptCatalog(database);
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.CreateAsync(name, prompt, default));
        Assert.Empty(database.ChangeTracker.Entries());
    }
}
