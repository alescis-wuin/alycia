namespace Alycia.Modules.Chat.Application;

public static class PassageClassification
{
    public static QueryBrief Brief(string question, IEnumerable<string> literals, IEnumerable<string> antecedents, int maxFacets = 4) =>
        PassageClassifier.Brief(question, literals, antecedents, maxFacets);
}
