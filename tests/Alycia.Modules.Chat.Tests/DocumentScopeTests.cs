using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Alycia.Modules.Chat.Tests;

public sealed class DocumentScopeTests
{
    [PostgresFact]
    public Task A_membership_change_and_document_replacement_cannot_mix_two_snapshots() =>
        ProjectCreationTests.WithDatabaseAsync(async (options, _) =>
        {
            await using var db = new ChatDbContext(options);
            var previous = new WorkspaceProfile { Id = Guid.NewGuid() };
            var next = new WorkspaceProfile { Id = Guid.NewGuid() };
            var chat = Conversation.Create("Membre", "demo", "");
            chat.AssignWorkspace(previous.Id);
            var oldFile = new Attachment { Id = Guid.NewGuid(), WorkspaceId = previous.Id, Name = "ancien", BatchId = Guid.NewGuid() };
            var newFile = new Attachment { Id = Guid.NewGuid(), WorkspaceId = next.Id, Name = "nouveau", BatchId = Guid.NewGuid() };
            db.AddRange(previous, next, chat, oldFile);
            await db.SaveChangesAsync();
            async Task MoveAsync()
            {
                await using var second = new ChatDbContext(options);
                var owner = await second.Conversations.SingleAsync(c => c.Id == chat.Id);
                owner.AssignWorkspace(next.Id);
                second.Remove(await second.Attachments.SingleAsync(f => f.Id == oldFile.Id));
                second.Add(newFile);
                await second.SaveChangesAsync();
            }
            var scope = new DocumentScope(session => new InterleavedLookup(new ConversationWorkspaceLookup(session), MoveAsync));
            var firstRead = await new TurnDocuments(db, scope).FilesAsync(chat.Id, default);
            Assert.Equal(oldFile.Id, Assert.Single(firstRead).Id);
            var nextRead = await new TurnDocuments(db, ChatFixture.DocumentScope).FilesAsync(chat.Id, default);
            Assert.Equal(newFile.Id, Assert.Single(nextRead).Id);
        });

    private sealed class InterleavedLookup(IConversationWorkspaceLookup inner, Func<Task> write) : IConversationWorkspaceLookup
    {
        public async Task<Guid?> FindWorkspaceAsync(Guid conversation, CancellationToken ct)
        {
            var workspace = await inner.FindWorkspaceAsync(conversation, ct);
            await write();
            return workspace;
        }
    }
}
