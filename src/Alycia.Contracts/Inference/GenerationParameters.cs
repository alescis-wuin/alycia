using System.Text.Json;
using Alycia.Contracts.Chat;

namespace Alycia.Contracts.Inference;

public sealed record GenerationParameters(int ContextTokens, int MaxOutputTokens, bool GenerateUntilStop,
    double Temperature, int? Seed, int TimeoutSeconds, string CallPurpose, JsonElement? ResponseSchema) : IGenerationSettings;
