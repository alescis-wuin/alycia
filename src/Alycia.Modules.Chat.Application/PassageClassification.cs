namespace Alycia.Modules.Chat.Application;

public static class PassageClassification
{
    public static (IReadOnlyList<SourceChunk> Ranked, ClassificationReport Report) Apply(string original, QueryBrief brief,
        IReadOnlyList<SourceChunk> ranked, ChatOptions options, IReadOnlyDictionary<string, double>? semantic = null) =>
        PassageClassifier.Classify(original, brief, ranked, PassageRankingSettings.Capture(options, options.PinnedSourceIds), semantic);

    public static QueryBrief Brief(string question, IEnumerable<string> literals, IEnumerable<string> antecedents, int maxFacets = 4) =>
        PassageClassifier.Brief(question, literals, antecedents, maxFacets);
}
