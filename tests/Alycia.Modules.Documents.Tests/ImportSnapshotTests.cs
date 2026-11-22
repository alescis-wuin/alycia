using Alycia.Contracts.Chat;
using Alycia.Contracts.Documents;
using Alycia.Modules.Documents.Infrastructure;

namespace Alycia.Modules.Documents.Tests;

public sealed class ImportSnapshotTests
{
    [Fact]
    public async Task Replacing_an_input_after_admission_cannot_replace_the_authorized_document()
    {
        using var original = new MemoryStream("contenu"u8.ToArray());
        using var replacement = new MemoryStream("autre"u8.ToArray());
        IncomingFile[] files = [new("original.txt", original)];
        var batch = new DocumentImportBatch(Guid.NewGuid(), files, new ImportLimits());
        files[0] = new("autre.txt", replacement);
        var owner = Guid.NewGuid();
        var result = Assert.Single(await batch.PrepareAsync(owner, false, 2, default));
        Assert.Equal("original.txt", result.File.Name);
        Assert.Equal("contenu", result.File.Text);
        Assert.Equal(owner, result.File.ConversationId);
        Assert.Null(result.File.WorkspaceId);
    }
}
