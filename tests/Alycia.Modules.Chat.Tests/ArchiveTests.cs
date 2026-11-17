using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class ArchiveTests
{
    private static ConversationArchive Example() => new(1, "Projet", "demo", "Instructions", "Notes",
        new() { Temperature = 0.2, Seed = 42 },
        [new("user", "Mon code :\n```cs\nreturn 42;\n```", DateTimeOffset.UtcNow.AddMinutes(-1)),
            new("assistant", "Réponse 🙂", DateTimeOffset.UtcNow)]);

    [Fact]
    public void ExportImportPreservesContentsAndTimesWithFreshConversationAndRequestIds()
    {
        var original = Example();
        var archive = ConversationTransfer.Parse(ConversationTransfer.ExportJson(original));
        var first = ConversationTransfer.Restore(archive);
        var second = ConversationTransfer.Restore(archive);
        Assert.Equal(original.Messages, archive.Messages);
        Assert.Equal(42, archive.Options.Seed);
        Assert.Equal("Notes", first.Conversation.ContextNotes);
        Assert.Equal(2, first.Conversation.MessageCount);
        Assert.NotEqual(first.Conversation.Id, second.Conversation.Id);
        Assert.Equal(first.Messages[0].RequestId, first.Messages[1].RequestId);
        Assert.NotEqual(first.Messages[0].RequestId, second.Messages[0].RequestId);
        Assert.Equal(original.Messages[0].CreatedAtUtc, first.Messages[0].CreatedAtUtc);
        Assert.Contains("```cs\nreturn 42;\n```", ConversationTransfer.Markdown(archive));
    }

    [Fact]
    public void ToolEvidenceSurvivesRestoreAndCannotBeAttachedToUserMessages()
    {
        var trace = System.Text.Json.JsonSerializer.Serialize(new[] { new ToolObservation(1, "project_read", "{}", true, 50,
            new("src/A.cs", new string('a', 64), 200001, null, true)) }, ProjectToolCatalog.Json);
        var archive = Example();
        archive = archive with { Messages = [archive.Messages[0], archive.Messages[1] with { ToolTraceJson = trace }] };
        var restored = ConversationTransfer.Restore(ConversationTransfer.Parse(ConversationTransfer.ExportJson(archive)));
        Assert.Equal(trace, restored.Messages[1].ToolTraceJson);
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Validate(archive with
        { Messages = [archive.Messages[0] with { ToolTraceJson = trace }, archive.Messages[1]] }));
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Validate(archive with
        { Messages = [archive.Messages[0], archive.Messages[1] with { ToolTraceJson = "[null]" }] }));
    }

    [Fact]
    public void RejectsUnsupportedVersionOrIncompleteOrInvalidTurns()
    {
        var archive = Example();
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Validate(archive with { SchemaVersion = 2 }));
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Validate(archive with { Messages = [archive.Messages[0]] }));
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Validate(archive with
        { Messages = [archive.Messages[1], archive.Messages[0]] }));
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Validate(archive with
        { Messages = [archive.Messages[0] with { Content = "bad\0" }, archive.Messages[1]] }));
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Parse(new byte[ConversationTransfer.MaxBytes + 1]));
        Assert.Throws<ArgumentException>(() => ConversationTransfer.Parse("{}"u8.ToArray()));
    }
}
