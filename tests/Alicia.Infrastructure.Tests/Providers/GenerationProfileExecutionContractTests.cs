using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Application.Generations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Providers;
using Alicia.Infrastructure.Providers.LlamaCpp;

namespace Alicia.Infrastructure.Tests.Providers;

public sealed class GenerationProfileExecutionContractTests
{
    private const string TestApiKey = "A1B2C3D4E5F60718293A4B5C6D7E8F90";

    [Fact]
    public async Task RegistryRoutesBoundRequestBySnapshotProviderInsteadOfGlobalSelection()
    {
        StubProviderRuntime firstRuntime = new();
        StubProviderRuntime secondRuntime = new();
        StubStreamingResponder firstResponder = new("first");
        StubStreamingResponder secondResponder = new("second");
        InferenceProviderRegistry registry = new(
            new List<InferenceProviderRegistration>
            {
                Registration("provider.one", "One", firstRuntime, firstResponder),
                Registration("provider.two", "Two", secondRuntime, secondResponder),
            });
        registry.SelectProvider("provider.one");
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId revisionId = MessageRevisionId.New();
        GenerationSnapshot snapshot = new(
            GenerationSnapshotId.New(),
            conversationId,
            messageId,
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            new InferenceProviderConfiguration(
                "provider.two",
                "owner/model"),
            GenerationProfileId.New(),
            GenerationProfileRevisionId.New());
        ConversationResponseRequest request = CreateRequest(
            conversationId,
            messageId,
            snapshot);

        await ConsumeAsync(registry.StreamAsync(
            request,
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        Assert.Equal("provider.one", registry.SelectedProviderId);
        Assert.Equal(0, firstResponder.CallCount);
        Assert.Equal(1, secondResponder.CallCount);
    }

    [Fact]
    public async Task ChatClientPrependsResolvedProfileSystemInstructions()
    {
        CapturingHttpMessageHandler handler = new();
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        ConversationResponseRequest request = CreateRequest(
            conversationId,
            messageId,
            generationSnapshot: null,
            systemInstructions: "Follow the selected profile exactly.");

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            TestApiKey,
            request,
            new InferenceGenerationOptions(),
            TestContext.Current.CancellationToken)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement messages = document.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal(
            "Follow the selected profile exactly.",
            messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public void LlamaRuntimeUsesBoundSnapshotSamplingInsteadOfActiveGlobalSampling()
    {
        InferenceProviderConfiguration activeConfiguration = new(
            LlamaCppProviderRuntime.ProviderId,
            "owner/model-GGUF:Q4_K_M",
            contextSize: 8192,
            generation: new InferenceGenerationOptions(temperature: 0.95));
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        InferenceGenerationOptions profileOptions = new(
            maxOutputTokens: 256,
            temperature: 0.25,
            topP: 0.88);
        MessageRevisionId revisionId = MessageRevisionId.New();
        GenerationSnapshot snapshot = new(
            GenerationSnapshotId.New(),
            conversationId,
            messageId,
            revisionId,
            new[] { revisionId },
            DateTimeOffset.UtcNow,
            new InferenceProviderConfiguration(
                LlamaCppProviderRuntime.ProviderId,
                " owner/model-GGUF:Q4_K_M ",
                contextSize: 8192,
                generation: profileOptions),
            GenerationProfileId.New(),
            GenerationProfileRevisionId.New());
        ConversationResponseRequest request = CreateRequest(
            conversationId,
            messageId,
            snapshot);

        InferenceGenerationOptions resolved =
            LlamaCppProviderRuntime.ResolveGenerationOptionsForRequest(
                request,
                activeConfiguration);

        Assert.Equal(profileOptions, resolved);
        Assert.NotSame(profileOptions, resolved);
        Assert.Equal(0.25, resolved.Temperature);
        Assert.NotEqual(activeConfiguration.Generation.Temperature, resolved.Temperature);
    }

    [Fact]
    public void LlamaRuntimeRejectsBoundSnapshotForDifferentLoadedModelOrContext()
    {
        InferenceProviderConfiguration activeConfiguration = new(
            LlamaCppProviderRuntime.ProviderId,
            "owner/model-GGUF:Q4_K_M",
            contextSize: 4096);
        ConversationId conversationId = ConversationId.New();
        MessageId messageId = MessageId.New();
        MessageRevisionId modelRevisionId = MessageRevisionId.New();
        GenerationSnapshot differentModelSnapshot = new(
            GenerationSnapshotId.New(),
            conversationId,
            messageId,
            modelRevisionId,
            new[] { modelRevisionId },
            DateTimeOffset.UtcNow,
            new InferenceProviderConfiguration(
                LlamaCppProviderRuntime.ProviderId,
                "owner/other-GGUF:Q4_K_M",
                contextSize: 4096),
            GenerationProfileId.New(),
            GenerationProfileRevisionId.New());
        MessageRevisionId contextRevisionId = MessageRevisionId.New();
        GenerationSnapshot differentContextSnapshot = new(
            GenerationSnapshotId.New(),
            conversationId,
            messageId,
            contextRevisionId,
            new[] { contextRevisionId },
            DateTimeOffset.UtcNow,
            new InferenceProviderConfiguration(
                LlamaCppProviderRuntime.ProviderId,
                "owner/model-GGUF:Q4_K_M",
                contextSize: 8192),
            GenerationProfileId.New(),
            GenerationProfileRevisionId.New());

        InferenceProviderException modelException = Assert.Throws<InferenceProviderException>(() =>
            LlamaCppProviderRuntime.ResolveGenerationOptionsForRequest(
                CreateRequest(conversationId, messageId, differentModelSnapshot),
                activeConfiguration));
        InferenceProviderException contextException = Assert.Throws<InferenceProviderException>(() =>
            LlamaCppProviderRuntime.ResolveGenerationOptionsForRequest(
                CreateRequest(conversationId, messageId, differentContextSnapshot),
                activeConfiguration));

        Assert.Equal(InferenceProviderFailureKind.Model, modelException.Kind);
        Assert.Equal(InferenceProviderFailureKind.Model, contextException.Kind);
        Assert.Contains("loaded model", modelException.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("loaded model", contextException.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LlamaRuntimeKeepsLegacyActiveGenerationOptionsWithoutSnapshot()
    {
        InferenceProviderConfiguration activeConfiguration = new(
            LlamaCppProviderRuntime.ProviderId,
            "owner/model-GGUF",
            generation: new InferenceGenerationOptions(
                maxOutputTokens: 77,
                temperature: 0.61));
        ConversationResponseRequest request = CreateRequest(
            ConversationId.New(),
            MessageId.New(),
            generationSnapshot: null);

        InferenceGenerationOptions resolved =
            LlamaCppProviderRuntime.ResolveGenerationOptionsForRequest(
                request,
                activeConfiguration);

        Assert.Equal(activeConfiguration.Generation, resolved);
        Assert.NotSame(activeConfiguration.Generation, resolved);
    }

    private static ConversationResponseRequest CreateRequest(
        ConversationId conversationId,
        MessageId messageId,
        GenerationSnapshot? generationSnapshot,
        string? systemInstructions = null)
    {
        MessageRevisionId revisionId = generationSnapshot?.TriggeringUserMessageRevisionId
            ?? MessageRevisionId.New();
        List<ChatMessage> messages = new()
        {
            new ChatMessage(
                messageId,
                revisionId,
                parentRevisionId: null,
                MessageRole.User,
                "Hello",
                new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero)),
        };
        return new ConversationResponseRequest(
            conversationId,
            "Generation contract",
            messages,
            generationSnapshot,
            systemInstructions);
    }

    private static async Task ConsumeAsync(
        IAsyncEnumerable<ConversationResponseChunk> chunks)
    {
        await foreach (ConversationResponseChunk _ in chunks.ConfigureAwait(true))
        {
        }
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

    private sealed class StubProviderRuntime : IInferenceProviderRuntime
    {
        public Task<InferenceProviderSnapshot> DetectAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSnapshot(InferenceProviderState.Ready));
        }

        public Task<InferenceProviderSnapshot> InstallAsync(
            IProgress<InferenceProviderProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSnapshot(InferenceProviderState.Ready));
        }

        public Task<InferenceProviderSnapshot> StartAsync(
            InferenceProviderConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSnapshot(InferenceProviderState.Running));
        }

        public Task<InferenceProviderSnapshot> StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateSnapshot(InferenceProviderState.Ready));
        }

        private static InferenceProviderSnapshot CreateSnapshot(
            InferenceProviderState state)
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

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? null
                : await request.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: [DONE]\n\n",
                    Encoding.UTF8,
                    "text/event-stream"),
            };
        }
    }
}
