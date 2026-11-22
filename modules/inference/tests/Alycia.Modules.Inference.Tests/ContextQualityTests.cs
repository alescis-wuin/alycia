
namespace Alycia.Modules.Chat.Tests;

public sealed class ContextQualityTests
{
    [Fact]
    public void EffectiveWindowProtectsSchemasOutputAndSafety()
    {
        var options = new ChatSettings { ContextTokens = 8192, MaxOutputTokens = 300, SafetyTokens = 256, GenerateUntilStop = false, AdaptiveInferenceEnabled = true };
        PromptMessage[] input = [new("user", "Bonjour")];
        Assert.Equal(1024, InferenceWindow.ForCall(InferenceCall.Capture(options), input, false).Generation.ContextTokens);
        var tools = InferenceWindow.ForCall(InferenceCall.Capture(options), input, true);
        Assert.InRange(tools.Generation.ContextTokens, PromptTokens.InputCost(input, null, true) + 556, 8192);
        Assert.Throws<ArgumentException>(() => InferenceWindow.ForCall(InferenceCall.Capture(options), [new("user", new string('a', 8000))], false));
        options.GenerateUntilStop = true;
        Assert.Equal(8192, InferenceWindow.ForCall(InferenceCall.Capture(options), input, false).Generation.ContextTokens);
    }
}
