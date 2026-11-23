namespace Alycia.Contracts.Chat;

public sealed record QueryHit(int Query, int Rank, double Lexical, double? Semantic);
public sealed record RerankReport(string State, string? Identity, double Milliseconds, IReadOnlyDictionary<string, double>? Scores = null);
public sealed record RetrievalCacheReport(bool LexicalHit, int VectorHits, int VectorMisses, bool VectorWritten);
public sealed record RetrievalDecision(string Reference, Guid FileId, string Sha256, int Start, int End, double LexicalScore, string Reason, QueryHit[]? Queries = null);
public sealed record RetrievalReport(string Mode, int CandidateChunks, int IndexedFiles, int EligibleFiles, IReadOnlyList<string> Omissions,
    int ZeroScoreChunks = 0, double IndexMilliseconds = 0, double RankingMilliseconds = 0,
    IReadOnlyList<RetrievalDecision>? Decisions = null, int UnreportedDecisions = 0, RetrievalCacheReport? Cache = null, RerankReport? Rerank = null, SearchQueryPlan? Plan = null, ClassificationReport? Classification = null);
public sealed record SearchTerm(string Text, string Kind);
public sealed record ResolvedReferent(string Text, int Sequence, string Reference);
public sealed record SearchQueryPlan(string Original, SearchTerm[] Queries, ResolvedReferent[] Resolved, bool Ambiguous, QueryBrief? Brief = null);
public sealed record QuestionFacet(string Id, string Question, string Lexical, string Semantic);
public sealed record QueryBrief(string Summary, string[] Labels, string[] Literals, string[] Dates, QuestionFacet[] Facets, string? Hypothesis, string Original = "", string[]? Constraints = null, string Intent = "information",
    string Language = "und", string[]? Categories = null, string Method = "rules-v1");
public sealed record PassageJudgment(string Reference, string Relevance, string Relation, double OriginalScore,
    double SummaryScore, IReadOnlyDictionary<string, double> Facets, bool Admitted);
public sealed record ClassificationReport(string Method, int Pairs, int Unexamined, PassageJudgment[] Candidates);
public sealed record QueryRoute(string Mode, string Reason, bool Search, bool Tools, string Rules = "route-v1");
