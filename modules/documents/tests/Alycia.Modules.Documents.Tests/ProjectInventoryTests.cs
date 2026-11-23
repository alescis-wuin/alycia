using Alycia.Modules.Documents;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectInventoryTests
{
    [Fact]
    public void TreeIsDeterministicAndDoesNotInventMissingDates()
    {
        var first = new Attachment { Id = Guid.NewGuid(), Name = "src/deep/code.cs", Size = 32, Sha256 = "ABC", ContextMode = "excluded" };
        var second = new Attachment
        {
            Id = Guid.NewGuid(),
            Name = "bundle.zip!/note.md",
            Size = 12,
            Sha256 = "DEF",
            SourceModifiedAt = "2025-02-03T12:00:00",
            MetadataSource = "ZIP: heure locale, fuseau non fourni"
        };
        var tree = ProjectInventory.Tree(DocumentInventory.Describe([second, first]));
        Assert.Equal(tree, ProjectInventory.Tree(DocumentInventory.Describe([first, second])));
        Assert.Contains("├── bundle.zip!/", tree);
        Assert.Contains("extension=.cs", tree);
        Assert.Contains("SHA-256=ABC", tree);
        Assert.Contains("création=indisponible", tree);
        Assert.Contains("modification=2025-02-03T12:00:00", tree);
        Assert.Contains("contexte=excluded", tree);
        Assert.Contains("src/deep/code.cs", tree);
    }

    [Fact]
    public void FingerprintChangesWhenContentOrExclusionChanges()
    {
        var file = new Attachment { Id = Guid.NewGuid(), Name = "note.txt", Sha256 = "A" };
        var first = ProjectInventory.Fingerprint(DocumentInventory.Describe([file]));
        file.Sha256 = "B";
        Assert.NotEqual(first, ProjectInventory.Fingerprint(DocumentInventory.Describe([file])));
        file.Sha256 = "A"; file.ContextMode = "excluded";
        Assert.NotEqual(first, ProjectInventory.Fingerprint(DocumentInventory.Describe([file])));
    }
}
