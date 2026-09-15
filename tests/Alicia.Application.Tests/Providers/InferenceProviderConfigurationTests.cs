using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceProviderConfigurationTests
{
    [Fact]
    public void ConfigurationNormalizesProviderAndModelAndPreservesUnsetDefaults()
    {
        InferenceProviderConfiguration configuration = new(
            " llama.cpp.cuda ",
            " owner/model-GGUF:Q4_K_M ");

        Assert.Equal("llama.cpp.cuda", configuration.ProviderId);
        Assert.Equal("owner/model-GGUF:Q4_K_M", configuration.ModelReference);
        Assert.Null(configuration.ContextSize);
        Assert.Null(configuration.Generation.ReasoningEnabled);
        Assert.Null(configuration.Generation.ReasoningBudgetTokens);
        Assert.True(configuration.Generation.UsesOnlyProviderDefaults);
        Assert.True(configuration.UsesProviderDefaults);
    }

    [Fact]
    public void GenerationOptionsPreserveExplicitValues()
    {
        InferenceGenerationOptions generation = new(
            maxOutputTokens: 512,
            temperature: 0.7,
            topP: 0.9,
            topK: 40,
            seed: 42,
            reasoningEnabled: true,
            reasoningBudgetTokens: 384);
        InferenceProviderConfiguration configuration = new(
            "llama.cpp.cuda",
            "owner/model-GGUF",
            contextSize: 8192,
            generation: generation);

        Assert.Equal(8192, configuration.ContextSize);
        Assert.Equal(512, generation.MaxOutputTokens);
        Assert.Equal(0.7, generation.Temperature);
        Assert.Equal(0.9, generation.TopP);
        Assert.Equal(40, generation.TopK);
        Assert.Equal(42, generation.Seed);
        Assert.Equal(true, generation.ReasoningEnabled);
        Assert.Equal(384, generation.ReasoningBudgetTokens);
        Assert.False(configuration.UsesProviderDefaults);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ContextSizeRejectsNonPositiveValues(int contextSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceProviderConfiguration(
                "provider",
                "model",
                contextSize));
    }

    [Fact]
    public void GenerationOptionsRejectOutOfRangeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(maxOutputTokens: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(temperature: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(temperature: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(topP: 1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(topK: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(seed: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceGenerationOptions(reasoningEnabled: true, reasoningBudgetTokens: 0));
        Assert.Throws<ArgumentException>(() =>
            new InferenceGenerationOptions(reasoningEnabled: false, reasoningBudgetTokens: 128));
    }

    [Fact]
    public void DescriptorRequiresStableIdentifierAndDisplayName()
    {
        InferenceProviderDescriptor descriptor = new(
            " provider.local ",
            " Local provider ");

        Assert.Equal("provider.local", descriptor.Id);
        Assert.Equal("Local provider", descriptor.Name);
        Assert.Throws<ArgumentException>(() => new InferenceProviderDescriptor("", "Name"));
        Assert.Throws<ArgumentException>(() => new InferenceProviderDescriptor("id", " "));
    }
}
