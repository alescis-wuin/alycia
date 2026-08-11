using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceProviderSnapshotTests
{
    [Fact]
    public void ConstructorNormalizesOptionalText()
    {
        Uri endpoint = new("http://127.0.0.1:8080/");
        InferenceProviderSnapshot snapshot = new(
            "  llama.cpp CUDA  ",
            InferenceProviderState.Running,
            version: "  b9000  ",
            isCudaEnabled: true,
            executablePath: "  /tmp/llama-server  ",
            modelReference: "  owner/model-GGUF:Q4_K_M  ",
            endpoint: endpoint,
            detail: "  Ready  ");

        Assert.Equal("llama.cpp CUDA", snapshot.Name);
        Assert.Equal("b9000", snapshot.Version);
        Assert.Equal("/tmp/llama-server", snapshot.ExecutablePath);
        Assert.Equal("owner/model-GGUF:Q4_K_M", snapshot.ModelReference);
        Assert.Equal("Ready", snapshot.Detail);
        Assert.Equal(endpoint, snapshot.Endpoint);
        Assert.True(snapshot.IsCudaEnabled);
    }

    [Fact]
    public void RunningProviderRequiresEndpoint()
    {
        Assert.Throws<ArgumentException>(() =>
            new InferenceProviderSnapshot(
                "llama.cpp CUDA",
                InferenceProviderState.Running));
    }

    [Fact]
    public void ConstructorRejectsBlankProviderName()
    {
        Assert.Throws<ArgumentException>(() =>
            new InferenceProviderSnapshot(
                "   ",
                InferenceProviderState.Missing));
    }

    [Fact]
    public void ProgressNormalizesTextAndAcceptsFraction()
    {
        InferenceProviderProgress progress = new(
            "  Compiling llama-server  ",
            "  [42/100] Building CUDA object  ",
            0.42);

        Assert.Equal("Compiling llama-server", progress.Stage);
        Assert.Equal("[42/100] Building CUDA object", progress.Detail);
        Assert.Equal(0.42, progress.Fraction!.Value);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void ProgressRejectsFractionOutsideUnitInterval(double fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InferenceProviderProgress(
                "Compiling",
                "Build progress",
                fraction));
    }
}
