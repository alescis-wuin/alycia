using Alycia.Contracts.Retrieval;
using Alycia.Modules.Testing;

namespace Alycia.Modules.Retrieval.Tests;

public sealed class ScoringContractTests
{
    [Fact]
    public void Lexical_adapters_preserve_positions_dimension_and_rank_relevant_files_first()
    {
        static SourceChunk Chunk(string name, string text) => new(Guid.NewGuid(), name, "hash", "", "text", 0, text.Length, 1, 1, text, [], true);
        var wanted = Chunk("moteur.txt", "turbo compresseur turbine");
        var other = Chunk("recette.txt", "farine sucre");
        SourceChunk[] chunks = [other, wanted];
        foreach (var scoring in new IPassageScoring[] { new PassageScoring(), new FixturePassageScoring() })
        {
            var scores = scoring.Bm25(chunks, "turbo");
            Assert.Equal(2, scores.Length);
            Assert.All(scores, s => Assert.True(double.IsFinite(s)));
            Assert.True(scores[1] > scores[0]);
            Assert.Equal(wanted.FileId, scoring.Rank(chunks, "turbo", scores)[0].FileId);
            Assert.Equal(other.FileId, scoring.Rank(chunks, "turbo", [2, 1])[0].FileId);
            Assert.Equal(other.FileId, chunks[0].FileId);
            Assert.Empty(scoring.Bm25([], "turbo"));
            Assert.Throws<ArgumentException>(() => scoring.Rank(chunks, "turbo", [1]));
        }
    }
}
