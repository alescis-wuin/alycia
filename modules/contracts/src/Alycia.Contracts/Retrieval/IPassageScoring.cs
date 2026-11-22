using Alycia.Contracts.Indexing;

namespace Alycia.Contracts.Retrieval;

public interface IPassageScoring
{
    double[] Bm25(IReadOnlyList<SourceChunk> chunks, string query);
    IReadOnlyList<SourceChunk> Rank(IReadOnlyList<SourceChunk> chunks, string query, double[] lexicalScores);
}
