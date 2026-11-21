using Alycia.Contracts.Retrieval;
namespace Alycia.Modules.Chat.Application;

public static class SemanticClaimAudit
{
    public static async Task<VerificationFollowup> SearchAsync(ClaimFinding gap, Guid owner, ChatOptions settings,
        IProjectRetrieval? retrieval, MeteredChatModel measured, CancellationToken ct)
    {
        if (!settings.VerificationSearchEnabled || retrieval is null || settings.RetrievalMode == "legacy" || settings.Route?.Search == false)
            return new("not_searched", gap.Text, []);
        var lookup = settings.Copy(); lookup.QueryRewriteEnabled = false; lookup.SearchPlan = null; lookup.RerankEnabled = false;
        lookup.RetrievalMode = "lexical"; lookup.PinnedSourceIds = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(3, settings.TimeoutSeconds)));
        try
        {
            var claim = PassageEvidenceAudit.ClaimText(gap.Text);
            var found = await retrieval.PrepareAsync(owner, claim, lookup.ProjectSearch(), timeout.Token);
            measured.AddCalls(found.Calls);
            var leads = found.Chunks.Where(c => c.Kind != "summary" && settings.EligibleSourceIds?.Contains(c.FileId) == true).Take(3)
                .Select(c => new SourceExcerpt(c.FileId, c.Path, System.Text.Encoding.UTF8.GetByteCount(c.Text), !c.CompleteUnit,
                    "recherche après génération", ProjectRetrieval.Reference(c), c.StartLine, c.EndLine, c.Sha256, c.Start, c.End, Content: c.Text)).ToArray();
            if (leads.Length == 0) return new("not_found", gap.Text, []);
            var assessed = leads.Select(s => (Source: s, Status: Check(claim + " [" + s.Reference + "]", [s], []).Findings.FirstOrDefault()?.Status)).ToArray();
            var supported = assessed.Where(p => p.Status == "supported").Select(p => p.Source).ToArray();
            var opposed = assessed.Any(p => p.Status == "contradicted");
            var state = supported.Length > 0 ? opposed ? "conflicting_evidence" : "supplementary_support" : opposed ? "supplementary_contradiction" : "inconclusive";
            var retained = state == "supplementary_support" ? supported : leads;
            return new(state, gap.Text, retained.Select(s => s.Reference!).ToArray(), retained);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or AccessViolationException))
        { return new("unavailable", gap.Text, []); }
    }
    public static ClaimReport Check(string answer, IReadOnlyList<SourceExcerpt>? sources, IReadOnlyList<EvidenceText>? other) =>
        PassageEvidenceAudit.Check(answer, sources, other);
}
