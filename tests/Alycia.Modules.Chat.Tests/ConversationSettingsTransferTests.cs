using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Tests;

public sealed class ConversationSettingsTransferTests
{
    private static byte[] Export() => ConversationSettingsTransfer.Export("demo", "Consigne\nDeuxième ligne", "Notes privées", new()
    {
        Temperature = .123456789,
        RelevantThreshold = .456789,
        NeutralThreshold = .123456,
        ToolMaxCalls = 17,
        ToolContextTokens = 2048,
        SummaryMaxCalls = 512,
        TimeoutSeconds = 411,
        RetrievalMode = "hybrid",
        EmbeddingModel = "embed:local",
        EmbeddingDocumentPrefix = "passage: ",
        Enrichment = new(true, "annotation:local", JobCalls: 127, Workers: 3, TimeoutSeconds: 377),
        Seed = -91,
        QuerySummaryTokens = 128,
        FacetPlanningEnabled = true,
        ModelIdentity = "runtime-only",
        CallPurpose = "annotation"
    });

    [Fact]
    public void RoundTripKeepsEveryPersistedOptionWithoutRoundingOrRuntimeData()
    {
        var bytes = Export();
        var settings = ConversationSettingsTransfer.Parse(bytes);
        Assert.Equal(bytes, ConversationSettingsTransfer.Export(settings.Model, settings.SystemPrompt, settings.ContextNotes, settings.Options));
        Assert.Equal(.123456789, settings.Options.Temperature);
        Assert.Equal(.456789, settings.Options.RelevantThreshold);
        Assert.Equal(377, settings.Options.Enrichment.TimeoutSeconds);
        Assert.Null(settings.Options.ModelIdentity);
        Assert.Equal("chat", settings.Options.CallPurpose);
        using var document = JsonDocument.Parse(bytes);
        Assert.False(document.RootElement.TryGetProperty("messages", out _));
        Assert.False(document.RootElement.TryGetProperty("workspaceId", out _));
        Assert.False(document.RootElement.GetProperty("options").TryGetProperty("modelIdentity", out _));
        Assert.DoesNotContain("runtime-only", Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("format")]
    [InlineData("missing-option")]
    [InlineData("missing-enrichment-option")]
    [InlineData("unknown-option")]
    [InlineData("null-notes")]
    [InlineData("invalid-budget")]
    [InlineData("null-options")]
    public void RejectsIncompatibleOrPartialFilesInsteadOfSilentlyUsingDefaults(string change)
    {
        var file = JsonNode.Parse(Export())!;
        switch (change)
        {
            case "version": file["version"] = 99; break;
            case "format": file["format"] = "alycia-conversation"; break;
            case "missing-option": file["options"]!.AsObject().Remove("temperature"); break;
            case "missing-enrichment-option": file["options"]!["enrichment"]!.AsObject().Remove("workers"); break;
            case "unknown-option": file["options"]!["newBudget"] = 12; break;
            case "null-notes": file["contextNotes"] = null; break;
            case "invalid-budget": file["options"]!["contextTokens"] = 1; break;
            case "null-options": file["options"] = null; break;
        }
        Assert.Throws<ArgumentException>(() => ConversationSettingsTransfer.Parse(Encoding.UTF8.GetBytes(file.ToJsonString())));
    }

    [Fact]
    public void RejectsDuplicateFieldsMalformedJsonAndOversizeFiles()
    {
        var text = Encoding.UTF8.GetString(Export()).Replace("\"version\": 1", "\"version\": 1, \"version\": 1");
        Assert.Throws<ArgumentException>(() => ConversationSettingsTransfer.Parse(Encoding.UTF8.GetBytes(text)));
        Assert.Throws<ArgumentException>(() => ConversationSettingsTransfer.Parse("{broken"u8.ToArray()));
        Assert.Throws<ArgumentException>(() => ConversationSettingsTransfer.Parse(new byte[ConversationSettingsTransfer.MaxBytes + 1]));
    }

    [Fact]
    public void AcceptsBomAndKeepsTargetIdentityAndHistoryWhenApplyingSettings()
    {
        var settings = ConversationSettingsTransfer.Parse(new byte[] { 0xef, 0xbb, 0xbf }.Concat(Export()).ToArray());
        var target = Conversation.Create("Discussion cible", "demo", "Ancienne instruction");
        var id = target.Id;
        var project = Guid.NewGuid(); target.AssignWorkspace(project); target.CompleteTurn();
        target.SetMemory("Mémoire conservée", 2, "hash", "demo");
        var revision = target.Revision;
        target.Configure(settings.Model, settings.SystemPrompt, settings.ContextNotes, ConversationConfiguration.Serialize(settings.Options));
        Assert.Equal(id, target.Id); Assert.Equal(project, target.WorkspaceId);
        Assert.Equal("Discussion cible", target.Title); Assert.Equal(2, target.MessageCount);
        Assert.Equal("Mémoire conservée", target.MemoryText);
        Assert.Equal(revision + 1, target.Revision);
        var persisted = ConversationConfiguration.Options(target, new());
        Assert.Equal(Export(), ConversationSettingsTransfer.Export(target.Model, target.SystemPrompt, target.ContextNotes, persisted));
    }
}
