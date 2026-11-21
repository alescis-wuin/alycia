namespace Alycia.Modules.Chat.Application;

public static class ModelConfiguration
{
    public static async Task<ChatOptions> PrepareOptionsAsync(this IChatModel provider, string model, ChatOptions settings, CancellationToken ct)
    {
        var result = settings.Copy();
        var profile = await provider.InspectAsync(model, result.TokenCounting, ct);
        if (profile.Identity is not null) result.ModelIdentity = profile.Identity;
        if (profile.Inspected) result.TokenCounter = profile.Counter;
        if (profile.ContextLimit is int limit) result.ContextTokens = Math.Min(result.ContextTokens, limit);
        return result;
    }
}
