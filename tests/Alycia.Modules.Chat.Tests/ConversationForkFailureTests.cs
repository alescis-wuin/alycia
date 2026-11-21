using System.Data.Common;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConversationForkFailureTests
{
    [PostgresFact]
    public Task RejectedInsertDeletesCopiesAndSameScopeCanRetry() => WithSourceAsync(async (options, storage, source) =>
    {
        await using var db = new ChatDbContext(options);
        var history = new ConversationHistory(db, new(), storage);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX fork_reject ON chat.attachments ("Sha256")""");

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => history.ForkAsync(source.Id, source.Revision, null, default));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        await AssertCompleteAsync(options, storage, source.Id);
        Assert.Equal([storage.DirectoryFor(source.Id)], Directory.GetDirectories(storage.Root));

        await db.Database.ExecuteSqlRawAsync("DROP INDEX chat.fork_reject");
        var retry = await history.ForkAsync(source.Id, source.Revision, null, default);
        await AssertCompleteAsync(options, storage, source.Id, retry.Id);
    });

    [PostgresFact]
    public Task DeferredConstraintRejectionAtCommitDeletesRolledBackFiles() => WithSourceAsync(async (options, storage, source) =>
    {
        await using var db = new ChatDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE chat.attachments ADD CONSTRAINT reject_fork UNIQUE ("Sha256") DEFERRABLE INITIALLY DEFERRED
            """);
        var history = new ConversationHistory(db, new(), storage);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => history.ForkAsync(source.Id, source.Revision, null, default));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, failure.SqlState);
        await AssertCompleteAsync(options, storage, source.Id);
        Assert.Equal([storage.DirectoryFor(source.Id)], Directory.GetDirectories(storage.Root));
    });

    [PostgresFact]
    public Task CancellationBeforeCommitRemovesOnlyFilesCreatedByTheFork() => WithSourceAsync(async (options, storage, source) =>
    {
        using var cancellation = new CancellationTokenSource();
        var interrupt = new CancelAfterCopy(storage, cancellation);
        var intercepted = new DbContextOptionsBuilder<ChatDbContext>(options).AddInterceptors(interrupt).Options;
        await using var db = new ChatDbContext(intercepted);
        var history = new ConversationHistory(db, new(), storage);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => history.ForkAsync(source.Id, source.Revision, null, cancellation.Token));

        await AssertCompleteAsync(options, storage, source.Id);
        Assert.NotNull(interrupt.Marker);
        Assert.Equal([interrupt.Marker], Directory.GetFiles(Path.GetDirectoryName(interrupt.Marker)!));
        Assert.Equal("autre écriture", await File.ReadAllTextAsync(interrupt.Marker));
    });

    [PostgresFact]
    public Task InconclusiveCommitKeepsFilesEvenWhenTheChildIsNotVisible() => WithSourceAsync(async (options, storage, source) =>
    {
        await using var writer = new ChatDbContext(options);
        await using var blocker = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("LOCK TABLE chat.conversations IN ROW EXCLUSIVE MODE");
        var intercepted = new DbContextOptionsBuilder<ChatDbContext>(options).AddInterceptors(new FailBeforeCommit()).Options;
        await using var db = new ChatDbContext(intercepted);
        var history = new ConversationHistory(db, new(), storage);
        await Assert.ThrowsAsync<IOException>(() => history.ForkAsync(source.Id, source.Revision, null, default));

        await AssertCompleteAsync(options, storage, source.Id);
        var retained = Assert.Single(Directory.GetDirectories(storage.Root), path => path != storage.DirectoryFor(source.Id));
        Assert.Equal("original", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(retained))));
    });

    [PostgresFact]
    public Task LostAcknowledgementAfterCommitKeepsEveryReferencedOriginal() => WithSourceAsync(async (options, storage, source) =>
    {
        var intercepted = new DbContextOptionsBuilder<ChatDbContext>(options).AddInterceptors(new FailAfterCommit()).Options;
        await using var db = new ChatDbContext(intercepted);
        var history = new ConversationHistory(db, new(), storage);
        await Assert.ThrowsAsync<IOException>(() => history.ForkAsync(source.Id, source.Revision, null, default));

        await using var verifier = new ChatDbContext(options);
        var child = await verifier.Conversations.SingleAsync(c => c.Id != source.Id);
        await AssertCompleteAsync(options, storage, source.Id, child.Id);
    });

    private sealed class CancelAfterCopy(FileStorage storage, CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public string? Marker { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var copy = eventData.Context!.ChangeTracker.Entries<Attachment>().Single().Entity;
            Assert.Equal("original", File.ReadAllText(storage.PathFor(copy)));
            Marker = Path.Combine(storage.DirectoryFor(copy.ConversationId!.Value), "autre.bin");
            File.WriteAllText(Marker, "autre écriture");
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailBeforeCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new IOException("Issue du commit indisponible.");
    }

    private sealed class FailAfterCommit : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => throw new IOException("Accusé de commit perdu.");
    }

    private static async Task AssertCompleteAsync(DbContextOptions<ChatDbContext> options, FileStorage storage, params Guid[] owners)
    {
        await using var db = new ChatDbContext(options);
        Assert.Equal(owners.Order(), (await db.Conversations.Select(c => c.Id).ToArrayAsync()).Order());
        Assert.Equal(owners.Length, await db.Attachments.CountAsync());
        Assert.Equal(owners.Length * 2, await db.Messages.CountAsync());
        foreach (var id in owners)
        {
            var file = await db.Attachments.SingleAsync(f => f.ConversationId == id);
            Assert.Equal("dossier/original.txt", file.Name);
            var path = Assert.Single(Directory.GetFiles(storage.DirectoryFor(id)));
            Assert.Equal(storage.PathFor(file), path);
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Equal(["Question", "Réponse"], await db.Messages.Where(m => m.ConversationId == id)
                .OrderBy(m => m.Sequence).Select(m => m.Content).ToArrayAsync());
        }
    }

    private static async Task WithSourceAsync(Func<DbContextOptions<ChatDbContext>, FileStorage, Conversation, Task> test)
    {
        var name = "alycia_fork_" + Guid.NewGuid().ToString("N");
        var connection = Environment.GetEnvironmentVariable("ALYCIA_CHAT_TEST_CONNECTION")!;
        await using var admin = new NpgsqlConnection(connection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin)) await create.ExecuteNonQueryAsync();
        var storage = new FileStorage(Path.Combine(Path.GetTempPath(), name));
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connection) { Database = name, Pooling = false }.ToString();
            var options = new DbContextOptionsBuilder<ChatDbContext>().UseNpgsql(isolated,
                pg => pg.MigrationsHistoryTable("__EFMigrationsHistory", "chat")).Options;
            await using var db = new ChatDbContext(options);
            await db.Database.MigrateAsync();
            var history = new ConversationHistory(db, new(), storage);
            var archive = new ConversationArchive(1, "Source", "demo", "", "", new(),
                [new("user", "Question", DateTimeOffset.UnixEpoch), new("assistant", "Réponse", DateTimeOffset.UnixEpoch)]);
            var source = await history.ImportAsync(ConversationTransfer.ExportJson(archive), default);
            await new FileImport(db, storage).ImportAsync(source.Id, Guid.NewGuid(),
                [new("dossier/original.txt", new MemoryStream("original"u8.ToArray()))], default);
            await test(options, storage, source);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            if (Directory.Exists(storage.Root)) Directory.Delete(storage.Root, true);
        }
    }
}
