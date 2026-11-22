using System.Text.RegularExpressions;
using Alycia.Contracts.Context;
using Alycia.Contracts.Tools;

namespace Alycia.Modules.Testing;

public sealed class FixtureResponseAudit(Func<Guid, string, CancellationToken, Task<VerificationFollowup?>>? followup = null) : ITurnResponseAudit
{
    public async Task<ReviewedReply> ReviewAsync(Guid id, GenerationReply generated, ContextWindow window, ClaimVerification verification,
        IModelMeter measured, Func<ModelDelta, Task>? onDelta, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(generated.Content) || generated.Content.Contains('\0')) throw new ChatModelException("Réponse vide ou invalide.");
        var evidence = (window.Sources ?? []).Where(s => s.Reference is not null && s.Sha256 is not null && s.Start.HasValue && s.End.HasValue && s.StartLine.HasValue && s.EndLine.HasValue)
            .Select(s => new SuppliedSource(s.Reference!, s.Id, s.Path, s.Sha256!, s.StartLine!.Value, s.EndLine!.Value, s.Start!.Value, s.End!.Value, "initial", s.Partial)).ToArray();
        var known = evidence.Select(e => e.Reference).Concat((window.Temporal?.Sources ?? []).Select(s => s.Reference)).ToHashSet(StringComparer.Ordinal);
        var ids = Regex.Matches(generated.Content, @"\[(S[0-9A-F]{12}|H[0-9]+:[0-9A-F]{12})\]", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups[1].Value.ToUpperInvariant()).Distinct().ToArray();
        var unknown = ids.Where(x => !known.Contains(x)).ToArray();
        var result = new CitationReport(unknown.Length > 0 ? "unknown_references" : ids.Length > 0 ? "references_recognized" :
            known.Count > 0 ? "sources_uncited" : "no_sources", evidence, ids.Where(known.Contains).ToArray(), unknown);
        var extra = verification.Enabled && followup is not null ? await followup(id, generated.Content, ct) : null;
        return new(generated.Content, result, new([], 0), extra);
    }
}
