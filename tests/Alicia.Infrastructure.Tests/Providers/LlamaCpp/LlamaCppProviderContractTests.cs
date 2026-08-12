using System.Net;
using System.Text;
using System.Text.Json;
using Alicia.Application.Conversations;
using Alicia.Application.Providers;
using Alicia.Domain.Conversations;
using Alicia.Infrastructure.Providers.LlamaCpp;

namespace Alicia.Infrastructure.Tests.Providers.LlamaCpp;

public sealed class LlamaCppProviderContractTests
{
    [Theory]
    [InlineData("ggml-org/gemma-3-1b-it-GGUF", "ggml-org/gemma-3-1b-it-GGUF")]
    [InlineData(" owner/model-GGUF:Q4_K_M ", "owner/model-GGUF:Q4_K_M")]
    public void ModelReferenceNormalizesSupportedHuggingFaceForm(
        string input,
        string expected)
    {
        Assert.Equal(expected, LlamaCppModelReference.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("model-only")]
    [InlineData("owner/")]
    [InlineData("owner/model: ")]
    [InlineData("owner/model with space")]
    [InlineData("owner//model")]
    [InlineData("owner/model:Q4:Q5")]
    [InlineData("owner/model:Q4/K")]
    public void ModelReferenceRejectsInvalidHuggingFaceForm(string input)
    {
        Assert.Throws<ArgumentException>(() => LlamaCppModelReference.Normalize(input));
    }

    [Fact]
    public void ServerCommandPassesModelDirectlyToHfWithoutOverridingGpuLayerDefaults()
    {
        string[] arguments = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF:Q5_K_M",
            "/tmp/alicia-llama.log");

        AssertOption(arguments, "-hf", "owner/model-GGUF:Q5_K_M");
        AssertOption(arguments, "--host", "127.0.0.1");
        AssertOption(arguments, "--port", "8080");
        Assert.DoesNotContain("--n-gpu-layers", arguments);
        AssertOption(arguments, "--alias", "alicia-local");
        Assert.Contains("--jinja", arguments);
        Assert.Contains("--no-mmproj", arguments);
    }

    [Fact]
    public void ServerCommandAddsContextSizeOnlyWhenExplicitlyConfigured()
    {
        string[] providerDefaults = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF",
            "/tmp/alicia-default.log");
        string[] configured = LlamaCppServerCommand.CreateArguments(
            "owner/model-GGUF",
            "/tmp/alicia-configured.log",
            contextSize: 8192);

        Assert.DoesNotContain("--ctx-size", providerDefaults);
        AssertOption(configured, "--ctx-size", "8192");
    }

    [Fact]
    public void InstallerConfiguresCudaServerWithHttpsModelDownloads()
    {
        LlamaCppBuildToolchain toolchain = new(
            "/usr/bin/cmake",
            "/usr/bin/gcc",
            "/usr/bin/g++",
            "/opt/cuda/bin/nvcc",
            "/usr/bin/nvidia-smi",
            "Ninja");

        IReadOnlyList<string> arguments = LlamaCppInstaller.CreateConfigureArguments(
            toolchain,
            "/tmp/llama-source",
            "/tmp/llama-build");

        Assert.Contains("-DGGML_CUDA=ON", arguments);
        Assert.Contains("-DLLAMA_BUILD_TOOLS=ON", arguments);
        Assert.Contains("-DLLAMA_BUILD_SERVER=ON", arguments);
        Assert.Contains("-DLLAMA_OPENSSL=ON", arguments);
        Assert.Contains("-DBUILD_SHARED_LIBS=OFF", arguments);
        Assert.DoesNotContain("-DLLAMA_BUILD_TOOLS=OFF", arguments);
        AssertOption(arguments, "-G", "Ninja");
        Assert.Contains("-DCMAKE_C_COMPILER=/usr/bin/gcc", arguments);
        Assert.Contains("-DCMAKE_CXX_COMPILER=/usr/bin/g++", arguments);
        Assert.Contains("-DCMAKE_CUDA_COMPILER=/opt/cuda/bin/nvcc", arguments);
    }

    [Theory]
    [InlineData("[1/20] Building CXX object", 0.05)]
    [InlineData("[ 75%] Linking CXX executable", 0.75)]
    public void InstallerParsesNinjaAndMakeProgress(
        string line,
        double expected)
    {
        Assert.True(LlamaCppInstaller.TryParseBuildFraction(line, out double fraction));
        Assert.Equal(expected, fraction, precision: 6);
    }

    [Theory]
    [InlineData("ninja: no work to do.")]
    [InlineData("Building llama-server")]
    [InlineData("[x/y] invalid")]
    public void InstallerIgnoresUnstructuredBuildProgress(string line)
    {
        Assert.False(LlamaCppInstaller.TryParseBuildFraction(line, out _));
    }

    [Fact]
    public void LatestReleaseParserRequiresHttpsTagAndTarball()
    {
        LlamaCppReleaseDescriptor descriptor = LlamaCppInstaller.ParseReleaseDescriptor(
            """
            {
              "tag_name": "b9999",
              "tarball_url": "https://api.github.com/repos/ggml-org/llama.cpp/tarball/b9999"
            }
            """);

        Assert.Equal("b9999", descriptor.TagName);
        Assert.Equal(Uri.UriSchemeHttps, descriptor.TarballUri.Scheme);

        Assert.Throws<InvalidDataException>(() =>
            LlamaCppInstaller.ParseReleaseDescriptor(
                "{\"tag_name\":\"b1\",\"tarball_url\":\"http://example.test/source\"}"));
        Assert.Throws<InvalidDataException>(() =>
            LlamaCppInstaller.ParseReleaseDescriptor(
                "{\"tag_name\":\"b1\",\"tarball_url\":\"https://example.test/source\"}"));
        Assert.Throws<InvalidDataException>(() =>
            LlamaCppInstaller.ParseReleaseDescriptor(
                "{\"tag_name\":\"../escape\",\"tarball_url\":\"https://example.test/source\"}"));
    }

    [Theory]
    [InlineData(
        "Available devices:\n  CUDA0: NVIDIA GeForce RTX 4070 (8192 MiB, 7000 MiB free)",
        true)]
    [InlineData("Available devices:\n  (none)", false)]
    [InlineData("Available devices:\n  Vulkan0: NVIDIA GeForce RTX 4070", false)]
    public void RuntimeProbeRecognizesActiveCudaBackend(string output, bool expected)
    {
        Assert.Equal(expected, LlamaCppProviderRuntime.OutputShowsCuda(output));
    }

    [Fact]
    public void RuntimeProbeParsesVersionLine()
    {
        Assert.Equal(
            "9637 (`abc`)",
            LlamaCppProviderRuntime.ParseVersion(
                "ggml_cuda_init: found 1 CUDA devices:\nversion: 9637 (`abc`)\nbuilt with GNU"));
    }

    [Fact]
    public async Task ChatClientStreamsOpenAiCompatibleContentDeltas()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            data: {"choices":[{"delta":{"role":"assistant"}}]}

            data: {"choices":[{"delta":{"content":"Hello"}}]}

            data: {"choices":[{"delta":{"content":" from CUDA"}}]}

            data: [DONE]

            """);
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        ConversationResponseRequest request = CreateRequest();
        List<string> chunks = [];

        await foreach (ConversationResponseChunk chunk in client
            .StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                request,
                new InferenceGenerationOptions(),
                CancellationToken.None)
            .ConfigureAwait(true))
        {
            chunks.Add(chunk.ContentDelta);
        }

        Assert.Equal("Hello from CUDA", string.Concat(chunks));
        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        Assert.True(document.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(
            "alicia-local",
            document.RootElement.GetProperty("model").GetString());
        Assert.False(document.RootElement.TryGetProperty("max_tokens", out _));
        Assert.False(document.RootElement.TryGetProperty("temperature", out _));
        Assert.False(document.RootElement.TryGetProperty("top_p", out _));
        Assert.False(document.RootElement.TryGetProperty("top_k", out _));
        Assert.False(document.RootElement.TryGetProperty("seed", out _));
        Assert.False(document.RootElement.TryGetProperty("reasoning_effort", out _));
        Assert.False(document.RootElement.TryGetProperty("thinking_budget_tokens", out _));
        Assert.False(document.RootElement.TryGetProperty("reasoning_format", out _));
        JsonElement messages = document.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ChatClientSerializesOnlyExplicitGenerationOverrides()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        InferenceGenerationOptions options = new(
            maxOutputTokens: 128,
            temperature: 0.55,
            topP: 0.92,
            topK: 32,
            seed: 1234);

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            CreateRequest(),
            options,
            CancellationToken.None)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        Assert.Equal(128, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(0.55, root.GetProperty("temperature").GetDouble());
        Assert.Equal(0.92, root.GetProperty("top_p").GetDouble());
        Assert.Equal(32, root.GetProperty("top_k").GetInt32());
        Assert.Equal(1234, root.GetProperty("seed").GetInt32());
    }

    [Fact]
    public async Task ChatClientStreamsReasoningAndContentDeltasSeparately()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            """
            data: {"choices":[{"delta":{"reasoning_content":"Inspect premise"}}]}

            data: {"choices":[{"delta":{"reasoning_content":" then verify","content":"Final answer"}}]}

            data: [DONE]

            """);
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);
        List<ConversationResponseChunk> chunks = [];

        await foreach (ConversationResponseChunk chunk in client
            .StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                CreateRequest(),
                new InferenceGenerationOptions(
                    reasoningEnabled: true,
                    reasoningBudgetTokens: 512),
                CancellationToken.None)
            .ConfigureAwait(true))
        {
            chunks.Add(chunk);
        }

        Assert.Collection(
            chunks,
            chunk =>
            {
                Assert.Equal(ConversationResponseChunkKind.Reasoning, chunk.Kind);
                Assert.Equal("Inspect premise", chunk.TextDelta);
            },
            chunk =>
            {
                Assert.Equal(ConversationResponseChunkKind.Reasoning, chunk.Kind);
                Assert.Equal(" then verify", chunk.TextDelta);
            },
            chunk =>
            {
                Assert.Equal(ConversationResponseChunkKind.Content, chunk.Kind);
                Assert.Equal("Final answer", chunk.TextDelta);
            });
    }

    [Fact]
    public async Task ChatClientDisablesReasoningExplicitlyWhenConfiguredOff()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            CreateRequest(),
            new InferenceGenerationOptions(reasoningEnabled: false),
            CancellationToken.None)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal(0, root.GetProperty("thinking_budget_tokens").GetInt32());
        Assert.False(root.TryGetProperty("reasoning_format", out _));
    }

    [Fact]
    public async Task ChatClientSendsBoundedSeparatedReasoningWhenConfiguredOn()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        await ConsumeAsync(client.StreamAsync(
            new Uri("http://127.0.0.1:8080/"),
            CreateRequest(),
            new InferenceGenerationOptions(
                reasoningEnabled: true,
                reasoningBudgetTokens: 384),
            CancellationToken.None)).ConfigureAwait(true);

        string requestBody = Assert.IsType<string>(handler.RequestBody);
        using JsonDocument document = JsonDocument.Parse(requestBody);
        JsonElement root = document.RootElement;
        Assert.Equal(384, root.GetProperty("thinking_budget_tokens").GetInt32());
        Assert.Equal("deepseek", root.GetProperty("reasoning_format").GetString());
        Assert.False(root.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task ChatClientRejectsTruncatedStreamSoPartialTextCannotBePersisted()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Contains("[DONE]", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientMapsMalformedSseJsonToInvalidData()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.OK,
            "data: {not-json}\n\ndata: [DONE]\n\n");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Contains("malformed JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatClientSurfacesServerFailureWithoutHidingStatus()
    {
        CapturingHttpMessageHandler handler = new(
            HttpStatusCode.ServiceUnavailable,
            "{\"error\":{\"message\":\"Loading model\"}}");
        using HttpClient httpClient = new(handler);
        LlamaCppChatClient client = new(httpClient);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConsumeAsync(client.StreamAsync(
                new Uri("http://127.0.0.1:8080/"),
                CreateRequest(),
                new InferenceGenerationOptions(),
                CancellationToken.None))).ConfigureAwait(true);

        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Loading model", exception.Message, StringComparison.Ordinal);
    }

    private static ConversationResponseRequest CreateRequest()
    {
        DateTimeOffset createdAt = new(2026, 8, 11, 20, 0, 0, TimeSpan.Zero);
        return new ConversationResponseRequest(
            ConversationId.New(),
            "Local model",
            [
                new ChatMessage(
                    MessageId.New(),
                    MessageRole.System,
                    "Be concise.",
                    createdAt),
                new ChatMessage(
                    MessageId.New(),
                    MessageRole.User,
                    "Hello",
                    createdAt.AddMinutes(1)),
            ]);
    }

    private static void AssertOption(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue)
    {
        int index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0);
        Assert.True(index + 1 < arguments.Count);
        Assert.Equal(expectedValue, arguments[index + 1]);
    }

    private static async Task ConsumeAsync(
        IAsyncEnumerable<ConversationResponseChunk> stream)
    {
        await foreach (ConversationResponseChunk _ in stream.ConfigureAwait(true))
        {
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public CapturingHttpMessageHandler(
            HttpStatusCode statusCode,
            string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

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

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "text/event-stream"),
                RequestMessage = request,
            };
        }
    }
}
