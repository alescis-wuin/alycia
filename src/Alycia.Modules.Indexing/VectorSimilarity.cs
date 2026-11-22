using Alycia.Contracts.Indexing;

namespace Alycia.Modules.Indexing;

public static class VectorSimilarity
{
    public static double Cosine(float[] a, float[] b) => VectorScores.Cosine(a, b);
}
