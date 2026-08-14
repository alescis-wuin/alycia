using Alicia.Application.Providers;

namespace Alicia.Application.Tests.Providers;

public sealed class InferenceProviderFailureTests
{
    [Fact]
    public void ProviderExceptionKeepsOnlyExplicitSafeMessageAtTopLevel()
    {
        InvalidOperationException diagnostic = new(
            "server log: /home/user/private/model.gguf token=secret");
        InferenceProviderException exception = new(
            InferenceProviderFailureKind.Model,
            "The selected model could not be loaded. Review the model settings and try again.",
            diagnostic);

        Assert.Equal(InferenceProviderFailureKind.Model, exception.Kind);
        Assert.Equal(
            "The selected model could not be loaded. Review the model settings and try again.",
            exception.UserMessage);
        Assert.DoesNotContain("/home/user", exception.UserMessage, StringComparison.Ordinal);
        Assert.Same(diagnostic, exception.InnerException);
    }

    [Theory]
    [InlineData(InferenceProviderState.Missing, InferenceProviderFailureKind.Missing)]
    [InlineData(InferenceProviderState.Unsupported, InferenceProviderFailureKind.Unsupported)]
    [InlineData(InferenceProviderState.Faulted, InferenceProviderFailureKind.Faulted)]
    public void FailureStatesReceiveDefaultClassification(
        InferenceProviderState state,
        InferenceProviderFailureKind expected)
    {
        InferenceProviderSnapshot snapshot = new("Provider", state);

        Assert.Equal(expected, snapshot.FailureKind);
    }

    [Theory]
    [InlineData(InferenceProviderFailureKind.Network)]
    [InlineData(InferenceProviderFailureKind.Model)]
    public void FaultedSnapshotCanCarrySpecificFailureClassification(
        InferenceProviderFailureKind failureKind)
    {
        InferenceProviderSnapshot snapshot = new(
            "Provider",
            InferenceProviderState.Faulted,
            failureKind: failureKind);

        Assert.Equal(failureKind, snapshot.FailureKind);
    }

    [Fact]
    public void ReadySnapshotRejectsFailureClassification()
    {
        Assert.Throws<ArgumentException>(() =>
            new InferenceProviderSnapshot(
                "Provider",
                InferenceProviderState.Ready,
                failureKind: InferenceProviderFailureKind.Network));
    }
}
