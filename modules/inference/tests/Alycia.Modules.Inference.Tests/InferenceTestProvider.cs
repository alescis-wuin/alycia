
namespace Alycia.Modules.Chat.Tests;

internal sealed class ProviderSettings : ChatSettings
{
    public string CallPurpose { get; init; } = "chat";
    public InferenceCall InferenceCall() => Alycia.Contracts.Inference.InferenceCall.Capture(this, CallPurpose);
}

internal static class InferenceTestProvider
{
    internal static InferenceScheduler Scheduler { get; } = new();
    internal static ForegroundActivity Foreground { get; } = new();
    private static readonly ModelTokenizationCache Tokenizers = new();

    internal static ChatGenerationAdapter Ollama(HttpClient http, ProviderSettings settings) =>
        new(new OllamaTextGeneration(http, Scheduler, Tokenizers), settings.InferenceCall());
}
