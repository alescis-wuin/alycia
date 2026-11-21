using Alycia.Contracts.Retrieval;
using Alycia.Modules.Chat.Domain;

namespace Alycia.Modules.Chat.Application;

public sealed record ReviewedReply(string Reply, CitationReport Citations, ClaimReport Claims, VerificationFollowup? Followup);

public interface ITurnResponseAudit
{
    Task<ReviewedReply> ReviewAsync(Guid id, GenerationReply generated, ContextWindow window, ChatOptions settings,
        IModelMeter measured, Func<ModelDelta, Task>? onDelta, CancellationToken cancellationToken);
}

public sealed class TurnResponseAudit(IProjectRetrieval? retrieval = null) : ITurnResponseAudit
{
    public async Task<ReviewedReply> ReviewAsync(Guid id, GenerationReply generated, ContextWindow window, ChatOptions settings,
        IModelMeter measured, Func<ModelDelta, Task>? onDelta, CancellationToken cancellationToken)
    {
        var reply = generated.Content;
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(reply) || reply.Contains('\0'))
        {
            throw new ChatModelException("Ollama a renvoyé une réponse vide ou invalide. Essayez un autre modèle.");
        }

        var citations = CitationAudit.Check(reply, window.Sources, generated.Tools, window.Temporal?.Sources);
        var admitted = citations.Supplied.Where(s => s.Origin != "tool_compacted").Select(s => s.Reference)
            .Concat((citations.Historical ?? []).Select(s => s.Reference)).ToHashSet(StringComparer.Ordinal);
        var suppliedTexts = (generated.EvidenceTexts ?? []).Where(e => admitted.Contains(e.Reference)).Concat(window.MemoryTexts ?? []).ToArray();
        var claims = settings.ClaimVerificationEnabled ? SemanticClaimAudit.Check(reply, window.Sources, suppliedTexts)
            : ClaimAudit.Check(reply, window.Sources, suppliedTexts);
        VerificationFollowup? followup = null;
        if (settings.ClaimVerificationEnabled && claims.Findings.FirstOrDefault(f => f.Status is "insufficient" or "contradicted") is { } gap)
        {
            followup = await SemanticClaimAudit.SearchAsync(gap, id, settings, retrieval, measured, cancellationToken);
            var note = "\n\nVérification après génération — " + (gap.Status == "contradicted" ? "une affirmation contredit le passage cité" : "une affirmation manque de preuve")
                + " : « " + FileContext.Utf8Prefix(gap.Text, 220) + " ».";
            if (followup.Status == "supplementary_support")
                note += " Une recherche complémentaire retrouve cette affirmation dans " + string.Join(", ", followup.References.Select(r => "[" + r + "]")) + ".";
            if (followup.Status == "supplementary_support")
            {
                var extra = CitationAudit.Check(note, followup.Sources, []);
                citations = citations with
                {
                    Supplied = citations.Supplied.Concat(extra.Supplied.Select(s => s with { Origin = "verification" })).DistinctBy(s => s.Reference).ToArray(),
                    Recognized = citations.Recognized.Concat(followup.References).Distinct().ToArray(),
                    Status = citations.Unknown.Length > 0 ? "unknown_references" : "references_recognized"
                };
            }
            reply += note;
            if (onDelta is not null) await onDelta(new(note));
        }
        return new(reply, citations, claims, followup);
    }

    internal static void AttachToolTrace(ChatMessage assistant, IReadOnlyList<ToolObservation>? tools)
    {
        if (tools is { Count: > 0 })
            assistant.SetToolTrace(System.Text.Json.JsonSerializer.Serialize(tools, ProjectToolCatalog.Json));
    }

    internal static void AttachDiagnostics(ChatMessage assistant, Conversation conversation, GenerationReply generated,
        ContextWindow window, ChatOptions settings, GenerationUsage usage, ReviewedReply review)
    {
        assistant.SetDiagnostics(System.Text.Json.JsonSerializer.Serialize(new TurnDiagnostics(usage, settings.RetrievalReport, window.Memory, review.Citations, generated.Warnings,
            review.Claims, conversation.WorkspaceId, conversation.ProjectSnapshot?.Revision, settings.Route, window.Assembly, window.Temporal, generated.Research, review.Followup, window.Guides, window.HistoryMessagesIncluded,
            window.Messages.Any(m => m.Role == "system")), ProjectToolCatalog.Json));
    }
}
