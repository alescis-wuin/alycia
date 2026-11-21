using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Alycia.Modules.Chat.Infrastructure.Documents;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class DocumentImportBatchTests
{
    [Fact]
    public async Task KeepsOwnerArchiveHashesAndTimestampsWhenPreparingExcludedAndEligibleSources()
    {
        var owner = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        using var input = new MemoryStream();
        using (var zip = new ZipArchive(input, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in new[] { ("./src/Foo.cs", "class Foo {}"), (".env.local", "fixture") })
            {
                var entry = zip.CreateEntry(path);
                entry.LastWriteTime = new DateTimeOffset(2022, 3, 4, 5, 6, 8, TimeSpan.Zero);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }
        input.Position = 0;
        var batch = new DocumentImportBatch(batchId,
            [new IncomingFile("sources.zip", input, SourceCreatedAt: "2021", MetadataSource: "fixture")], new());
        var prepared = await batch.PrepareAsync(owner, true, 7, default);

        Assert.Equal(3, prepared.Count);
        var root = prepared[0].File;
        var code = prepared[1].File;
        var excluded = prepared[2].File;
        Assert.Null(root.ArchiveId);
        Assert.Equal("2021", root.SourceCreatedAt);
        Assert.Equal("fixture", root.MetadataSource);
        Assert.Equal("sources.zip!/src/Foo.cs", code.Name);
        Assert.Equal("2022-03-04T05:06:08", code.SourceModifiedAt);
        Assert.Equal("ZIP: heure locale, fuseau non fourni", code.MetadataSource);
        Assert.Equal("project", code.ContextMode);
        Assert.Equal("class Foo {}", code.Text);
        Assert.Equal("sources.zip!/.env.local", excluded.Name);
        Assert.Equal("excluded", excluded.ContextMode);
        Assert.Empty(excluded.Text);
        Assert.Equal(root.Id, code.ArchiveId);
        Assert.Equal(root.Id, excluded.ArchiveId);
        Assert.All(prepared, item =>
        {
            Assert.Equal(owner, item.File.WorkspaceId);
            Assert.Null(item.File.ConversationId);
            Assert.Equal(batchId, item.File.BatchId);
            Assert.Equal(7, item.File.AnchorSequence);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(item.Bytes)), item.File.Sha256);
        });
    }

    [Fact]
    public async Task DistinguishesCorruptArchiveFromUnextractableDocument()
    {
        var owner = Guid.NewGuid();
        var bytes = "invalid file"u8.ToArray();
        using var archive = new MemoryStream(bytes);
        using var pdf = new MemoryStream(bytes);
        var malformed = new DocumentImportBatch(Guid.NewGuid(), [new("x.zip", archive)], new());
        await Assert.ThrowsAsync<ArgumentException>(() => malformed.PrepareAsync(owner, false, 1, default));
        var retained = new DocumentImportBatch(Guid.NewGuid(), [new("x.pdf", pdf)], new());
        var document = Assert.Single(await retained.PrepareAsync(owner, false, 1, default));
        Assert.Equal(bytes, document.Bytes);
        Assert.Equal(owner, document.File.ConversationId);
        Assert.Equal("window", document.File.ContextMode);
        Assert.Equal("Fichier conservé · extraction non disponible", document.File.Status);
        Assert.Empty(document.File.Text);
    }

    [Fact]
    public async Task CancelsWhileReadingOriginalBeforeReturningAnyPreparedDocuments()
    {
        using var source = new WaitingStream();
        using var cancellation = new CancellationTokenSource();
        var batch = new DocumentImportBatch(Guid.NewGuid(), [new("waiting.txt", source)], new());
        var preparing = batch.PrepareAsync(Guid.NewGuid(), false, 1, cancellation.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparing);
    }

    private sealed class WaitingStream : MemoryStream
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
