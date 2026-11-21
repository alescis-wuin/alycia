using Alycia.Modules.Chat.Application;
using Alycia.Modules.Chat.Application.Context;

namespace Alycia.Modules.Chat.Tests;

internal static class ChatFixture
{
    internal static GenerationGate Gate { get; } = new();
    internal static IForegroundActivity Foreground { get; } = new ForegroundActivity();
    internal static InferenceScheduler Scheduler { get; } = new();

    public static ChatGenerationAdapter Ollama(HttpClient http, ChatOptions options) =>
        new(new OllamaTextGeneration(http, Scheduler), options);

    public static ChatService Create(IChatRepository repository, IChatModel model, ChatOptions options) =>
        new(repository, model, options, new TurnContext(repository), new ConversationMemory(repository, ChatFixture.Gate),
            new TurnTools(), new TurnRouting(repository), new TurnResponseAudit(), Gate, Foreground);
}
