namespace Alycia.Modules.Chat.Application;

public sealed class ChatGenerationAdapter(ITextGeneration provider, ChatOptions defaults) : IChatModel
{
    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct) => provider.ListModelsAsync(ct);
    public Task<bool> SupportsToolsAsync(string model, CancellationToken ct) => provider.SupportsToolsAsync(model, ct);
    public Task<long?> ResidentVramAsync(string model, CancellationToken ct) => provider.ResidentVramAsync(model, ct);

    public async Task<ChatOptions> PrepareOptionsAsync(string model, ChatOptions settings, CancellationToken ct)
    {
        var result = settings.Copy();
        var profile = await provider.InspectAsync(model, result.TokenCounting, ct);
        if (profile.Identity is not null) result.ModelIdentity = profile.Identity;
        if (profile.Inspected) result.TokenCounter = profile.Counter;
        if (profile.ContextLimit is int limit) result.ContextTokens = Math.Min(result.ContextTokens, limit);
        return result;
    }

    public Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, CancellationToken ct) =>
        GenerateAsync(model, messages, defaults, ct);

    public async Task<string> GenerateAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings, CancellationToken ct) =>
        (await GenerateReplyAsync(model, messages, settings, ct)).Content;

    public async Task<GenerationReply> GenerateReplyAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings, CancellationToken ct) =>
        ToReply(await provider.GenerateAsync(model, messages, settings.GenerationParameters(), ct));

    public async Task<GenerationReply> StreamAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings,
        Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        ToReply(await provider.StreamAsync(model, messages, settings.GenerationParameters(), onDelta, false, ct));

    public async Task<GenerationReply> StreamWithToolsAsync(string model, IReadOnlyList<PromptMessage> messages, ChatOptions settings,
        Func<ModelDelta, Task> onDelta, CancellationToken ct) =>
        ToReply(await provider.StreamAsync(model, messages, settings.GenerationParameters(), onDelta, true, ct));

    private static GenerationReply ToReply(ModelReply reply) => new(reply.Content, reply.Stats, reply.ToolCalls, reply.Thinking);
}
