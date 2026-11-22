using Alycia.Contracts.Inference;

namespace Alycia.Contracts.Indexing;

public static class VectorScores
{
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0 || a.Any(x => !float.IsFinite(x)) || b.Any(x => !float.IsFinite(x)))
            throw new ChatModelException("Cache vectoriel incompatible. Réindexez avec le modèle choisi.");
        double dot = 0, left = 0, right = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            left += (double)a[i] * a[i];
            right += (double)b[i] * b[i];
        }
        return left == 0 || right == 0 ? 0 : dot / Math.Sqrt(left * right);
    }
}
