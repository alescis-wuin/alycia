using Alycia.Contracts.Retrieval;
using Alycia.Contracts.Indexing;
using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Domain;
using Alycia.Modules.Chat.Infrastructure;

namespace Alycia.Modules.Chat.Tests;

public sealed class ProjectRetrievalTests
{
    [Fact]
    public void NoMatchStaysEmptyUnlessExplicitlyPinned()
    {
        var c = Chunk("data.txt", "data", "orion quota");
        var file = new Attachment { Id = c.FileId, Name = c.Path, Text = c.Text, Sha256 = c.Sha256, ContextMode = "project" };
        var ranked = PassageSearch.Rank([c], "badminton Neptune");
        Assert.Empty(ranked);
        Assert.Empty(PassageContext.Pack(ranked, [file.ToTurnFile()], 1000, new ChatOptions().Context()).Sources);
        file.ContextMode = "pinned";
        Assert.Equal("épinglé explicitement", Assert.Single(PassageContext.Pack(ranked, [file.ToTurnFile()], 1000, new ChatOptions().Context()).Sources).Reason);
    }

    private static SourceChunk Chunk(string path, string symbol, string text) => new(Guid.NewGuid(), path, "hash", symbol, "method", 0, text.Length, 1, 1, text, [], true);
    [Fact]
    public void ExactSymbolRanksAboveUnrelatedDocumentationAndPackingDeduplicates()
    {
        var implementation = Chunk("src/Store.cs", "Store.ReadAsync()", "return await database.ReadAsync(id);");
        var docs = Chunk("README.md", "Introduction", "Alycia est une application de conversation locale.");
        var copy = implementation with { FileId = Guid.NewGuid(), Path = "copy.cs" };
        var ranked = PassageSearch.Rank([docs, implementation, copy], "Où est implémenté Store.ReadAsync ?");
        Assert.NotEqual(docs.FileId, ranked[0].FileId);
        var files = ranked.Select(c => new Attachment { Id = c.FileId, Name = c.Path, Sha256 = c.Sha256, Text = c.Text, ContextMode = "project" }).ToArray();
        var packed = PassageContext.Pack(ranked, files.Select(f => f.ToTurnFile()).ToArray(), 1500, new ChatOptions().Context());
        Assert.Single(packed.Sources, c => c.Path.EndsWith(".cs"));
        Assert.All(packed.Sources, s => Assert.NotNull(s.Reference));
        Assert.True(PromptTokens.Count(packed.Text, null) <= 1500);
    }
    [Fact]
    public void SemanticRankingCanRetrieveAParaphraseAndExclusionsAlwaysWin()
    {
        var answer = Chunk("src/A.cs", "Purge", "remove obsolete records");
        var other = Chunk("src/B.cs", "Color", "render red button");
        var ranked = PassageSearch.Rank([other, answer], "Supprimer les anciennes données", [.01, .99]);
        Assert.Equal(answer, ranked[0]);
        var files = new[] { new Attachment { Id = answer.FileId, Name = answer.Path, Sha256 = answer.Sha256, Text = answer.Text, ContextMode = "excluded" } };
        Assert.Empty(PassageContext.Pack(ranked, files.Select(f => f.ToTurnFile()).ToArray(), 1000, new ChatOptions().Context()).Sources);
    }

}
