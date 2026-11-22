using Alycia.Contracts.Retrieval;
namespace Alycia.Modules.Retrieval;

public static class SemanticClaimAudit
{
    public static async Task<VerificationResult> SearchAsync(ClaimFinding gap, Guid owner, ClaimVerification verification,
        IProjectRetrieval? retrieval, CancellationToken ct)
    {
        if (verification.Search is not { } search || retrieval is null)
            return new(new("not_searched", gap.Text, []), []);
        IReadOnlyList<ModelCallUsage> calls = [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(verification.TimeoutSeconds));
        try
        {
            var claim = PassageEvidenceAudit.ClaimText(gap.Text);
            var found = await retrieval.PrepareAsync(owner, claim, search, timeout.Token);
            calls = found.Calls;
            var leads = found.Chunks.Where(c => c.Kind != "summary" && search.Passages.EligibleSourceIds?.Contains(c.FileId) == true).Take(3)
                .Select(c => new SourceExcerpt(c.FileId, c.Path, System.Text.Encoding.UTF8.GetByteCount(c.Text), !c.CompleteUnit,
                    "recherche après génération", SourceCoordinates.Reference(c), c.StartLine, c.EndLine, c.Sha256, c.Start, c.End, Content: c.Text)).ToArray();
            if (leads.Length == 0) return new(new("not_found", gap.Text, []), calls);
            var assessed = leads.Select(s => (Source: s, Status: Check(claim + " [" + s.Reference + "]", [s], []).Findings.FirstOrDefault()?.Status)).ToArray();
            var supported = assessed.Where(p => p.Status == "supported").Select(p => p.Source).ToArray();
            var opposed = assessed.Any(p => p.Status == "contradicted");
            var state = supported.Length > 0 ? opposed ? "conflicting_evidence" : "supplementary_support" : opposed ? "supplementary_contradiction" : "inconclusive";
            var retained = state == "supplementary_support" ? supported : leads;
            return new(new(state, gap.Text, retained.Select(s => s.Reference!).ToArray(), retained), calls);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
        { return new(new("unavailable", gap.Text, []), calls); }
    }
    public static ClaimReport Check(string answer, IReadOnlyList<SourceExcerpt>? sources, IReadOnlyList<EvidenceText>? other) =>
        PassageEvidenceAudit.Check(answer, sources, other);
}
