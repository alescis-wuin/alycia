using System.Net;
using System.Net.Http;
using Alycia.Client.Avalonia;

namespace Alycia.Client.Avalonia.Tests;

public sealed class BackendHealthProbeTests
{
    private const string BaseUrl = "http://127.0.0.1:8080";

    [Theory]
    [InlineData(HttpStatusCode.OK, "Healthy", true, "Backend available.")]
    [InlineData(
        HttpStatusCode.OK,
        "Unexpected",
        false,
        "Unexpected backend health response.")]
    [InlineData(
        HttpStatusCode.ServiceUnavailable,
        "Unhealthy",
        false,
        "Backend returned HTTP 503.")]
    public async Task CheckAsync_ClassifiesHttpResponses(
        HttpStatusCode status,
        string body,
        bool expectedAvailability,
        string expectedMessage)
    {
        using var client = CreateClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                new Uri($"{BaseUrl}/healthz"),
                request.RequestUri);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            });
        });

        var probe = CreateProbe(client);
        var result = await probe.CheckAsync();

        Assert.Equal(expectedAvailability, result.IsAvailable);
        Assert.Equal(expectedMessage, result.Message);
    }

    [Fact]
    public async Task CheckAsync_ReportsNetworkFailure()
    {
        using var client = CreateClient((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new HttpRequestException("Simulated network failure.")));

        var probe = CreateProbe(client);
        var result = await probe.CheckAsync();

        Assert.False(result.IsAvailable);
        Assert.Equal("Cannot connect to the backend.", result.Message);
    }

    [Fact]
    public async Task CheckAsync_ReportsTimeout()
    {
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var probe = new BackendHealthProbe(
            client,
            BaseUrl,
            TimeSpan.FromMilliseconds(100));

        var result = await probe.CheckAsync();

        Assert.False(result.IsAvailable);
        Assert.Equal("Backend request timed out.", result.Message);
    }

    [Theory]
    [InlineData("invalid-url", "Invalid API base URL.")]
    [InlineData(
        "http://example.com",
        "HTTPS is required for non-local connections.")]
    public async Task CheckAsync_RejectsInvalidConfiguration(
        string baseUrl,
        string expectedMessage)
    {
        using var client = CreateClient((_, _) =>
            throw new InvalidOperationException(
                "HTTP must not be called for invalid configuration."));

        var probe = new BackendHealthProbe(
            client,
            baseUrl,
            TimeSpan.FromSeconds(5));

        var result = await probe.CheckAsync();

        Assert.False(result.IsAvailable);
        Assert.Equal(expectedMessage, result.Message);
    }

    private static BackendHealthProbe CreateProbe(HttpClient client)
    {
        return new BackendHealthProbe(
            client,
            BaseUrl,
            TimeSpan.FromSeconds(5));
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    {
        return new HttpClient(new StubHttpMessageHandler(send));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _send;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        {
            _send = send;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _send(request, cancellationToken);
        }
    }
}
