namespace Alycia.Modules.Retrieval;

public sealed class PassageScoring : IPassageScoring
{
    public double[] Bm25(IReadOnlyList<SourceChunk> chunks, string query) => PassageSearch.Bm25(chunks, query);
    public IReadOnlyList<SourceChunk> Rank(IReadOnlyList<SourceChunk> chunks, string query, double[] lexicalScores) =>
        PassageSearch.Rank(chunks, query, lexicalScores: lexicalScores);
}
