using System.Text.Json.Serialization;
using Alycia.Contracts.Chat;
using Alycia.Contracts.Retrieval;

namespace Alycia.Contracts.Tools;

public sealed record GenerationReply(string Content, GenerationStats? Stats = null, IReadOnlyList<ModelToolCall>? ToolCalls = null, string? Thinking = null, IReadOnlyList<ToolObservation>? Tools = null, IReadOnlyList<string>? Warnings = null,
    [property: JsonIgnore] IReadOnlyList<EvidenceText>? EvidenceTexts = null, ResearchReport? Research = null);
