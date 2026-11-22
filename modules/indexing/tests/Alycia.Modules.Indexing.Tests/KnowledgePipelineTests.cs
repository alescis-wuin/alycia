using Alycia.Contracts.Chat;
using Alycia.Modules.Indexing;
using System.Text.Json;

namespace Alycia.Modules.Chat.Tests;

[Collection("Foreground indexing")]
public sealed class KnowledgePipelineTests
{
    [Fact]
    public void AnnotationValidationRejectsForeignReferencesAndOverBudgetSummaries()
    {
        string[] refs = ["SABCDEF123456"];
        var original = IndexedKnowledge.Extract("test", "Fait daté 2025-02-16 🧭.", "document", refs, 128, null);
        Assert.Contains("2025-02-16", original.DateMentions);
        string Encode(UnitLabels note) => JsonSerializer.Serialize(note, ProjectToolCatalog.Json);
        Assert.NotNull(IndexedKnowledge.Validate(Encode(original), refs, 128, null));
        Assert.Null(IndexedKnowledge.Validate(Encode(original with { SourceUnitIds = ["H99:FOREIGN"] }), refs, 128, null));
        Assert.Null(IndexedKnowledge.Validate(Encode(original with { Summary = new string('a', 800) }), refs, 64, null));
        Assert.Null(IndexedKnowledge.Validate("{\"unsupported\":true}", refs, 128, null));
    }
}
