using System.Runtime.CompilerServices;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Providers;

namespace Alicia.Infrastructure.Tests.Providers;

public sealed class InferenceProviderRegistryTests
{
    [Fact]
    public void RegistryRequiresExplicitSelectionAndNeverFallsBack()
    {
        StubProviderRuntime runtime = new();
        InferenceProviderRegistry registry = CreateRegistry(runtime, "provider.one");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.StreamAsync(CreateRequest(), CancellationToken.None));

        Assert.Contains("No inference provider is selected", exception.Message, StringComparison.Ordinal);
        Assert.Null(registry.SelectedProviderId);
    }

    [Fact]
    public async Task RegistryRoutesStreamingOnlyToExplicitlySelectedProvider()
    {
        StubProviderRuntime firstRuntime = new();
        StubProviderRuntime secondRuntime = new();
        StubStreamingResponder firstResponder = new("first");
        StubStreamingResponder secondResponder = new("second");
        InferenceProviderRegistry registry = new(
        [
            Registration("provider.one", "One", firstRuntime, firstResponder),
            Registration("provider.two", "Two", secondRuntime, secondResponder),
        ]);

        registry.SelectProvider("provider.two");
        List<string> chunks = [];

        await foreach (ConversationResponseChunk chunk in registry
            .StreamAsync(CreateRequest(), CancellationToken.None)
            .ConfigureAwait(true))
        {
            chunks.Add(chunk.ContentDelta);
        }

        Assert.Equal("provider.two", registry.SelectedProviderId);
        Assert.Equal("second", string.Concat(chunks));
        Assert.Equal(0, firstResponder.CallCount);
        Assert.Equal(1, secondResponder.CallCount);
        Assert.Same(secondRuntime, registry.GetRequiredRuntime("provider.two"));
    }

    [Fact]
    public void RegistryRejectsUnknownAndDuplicateProviderIdentifiers()
    {
        StubProviderRuntime runtime = new();
        InferenceProviderRegistry registry = CreateRegistry(runtime, "provider.one");

        Assert.Throws<KeyNotFoundException>(() => registry.SelectProvider("provider.missing"));
        Assert.Throws<KeyNotFoundException>(() => registry.GetRequiredRuntime("provider.missing"));
        Assert.Throws<ArgumentException>(() => new InferenceProviderRegistry(
        [
            Registration("duplicate", "One", new StubProviderRuntime(), new StubStreamingResponder("one")),
            Registration("duplicate", "Two", new StubProviderRuntime(), new StubStreamingResponder("two")),
        ]));
    }

    private static InferenceProviderRegistry CreateRegistry(
        StubProviderRuntime runtime,
        string providerId)
    {
        return new InferenceProviderRegistry(
        [
            Registration(
                providerId,
                "Provider",
                runtime,
                new StubStreamingResponder("response")),
        ]);
    }

    private static InferenceProviderRegistration Registration(
        string providerId,
        string name,
        IInferenceProviderRuntime runtime,
        IStreamingConversationResponder responder)
    {
        return new InferenceProviderRegistration(
            new InferenceProviderDescriptor(providerId, name),
            runtime,
            responder);
    }

    private static ConversationResponseRequest CreateRequest()
    {
        DateTimeOffset createdAt = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
        return new ConversationResponseRequest(
            ConversationId.New(),
            "Registry",
            [
                new ChatMessage(
                    MessageId.New(),
                    MessageRole.User,
                    "Hello",
                    createdAt),
            ]);
    }

    private sealed class StubProviderRuntime : IInferenceProviderRuntime
    {
        public Task<InferenceProviderSnapshot> DetectAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot(InferenceProviderState.Ready));
        }

        public Task<InferenceProviderSnapshot> InstallAsync(
            IProgress<InferenceProviderProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot(InferenceProviderState.Ready));
        }

        public Task<InferenceProviderSnapshot> StartAsync(
            InferenceProviderConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot(InferenceProviderState.Running));
        }

        public Task<InferenceProviderSnapshot> StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot(InferenceProviderState.Ready));
        }

        private static InferenceProviderSnapshot Snapshot(InferenceProviderState state)
        {
            return new InferenceProviderSnapshot(
                "Stub",
                state,
                endpoint: state == InferenceProviderState.Running
                    ? new Uri("http://127.0.0.1:8080/")
                    : null);
        }
    }

    private sealed class StubStreamingResponder : IStreamingConversationResponder
    {
        private readonly string _response;

        public StubStreamingResponder(string response)
        {
            _response = response;
        }

        public int CallCount { get; private set; }

        public async IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
            ConversationResponseRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            await Task.Yield();
            yield return new ConversationResponseChunk(_response);
        }
    }
}
