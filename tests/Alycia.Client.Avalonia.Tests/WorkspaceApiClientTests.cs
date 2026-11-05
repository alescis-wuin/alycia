using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Client.Avalonia;

namespace Alycia.Client.Avalonia.Tests;

public sealed class WorkspaceApiClientTests
{
    private const string BaseUrl = "http://127.0.0.1:5187";
    private static readonly Guid WorkspaceId =
        Guid.Parse("02f3deb8-3ea7-4a83-b788-ba5f4462e210");

    [Fact]
    public async Task CreateAsync_TrimsNameAndReadsCreatedWorkspace()
    {
        using var http = CreateHttpClient(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri($"{BaseUrl}/api/workspaces"), request.RequestUri);
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var data = JsonDocument.Parse(json);
            Assert.Equal("Personal", data.RootElement.GetProperty("name").GetString());
            return Response(HttpStatusCode.Created, "WORKSPACE_CREATED", true,
                new { id = WorkspaceId, name = "Personal", createdAtUtc = "2026-11-05T21:00:00+00:00" });
        });

        var result = await CreateClient(http).CreateAsync("  Personal  ");

        Assert.Equal(WorkspaceOperationState.Success, result.State);
        Assert.Equal(WorkspaceId, result.Workspace!.Id);
        Assert.Equal("Personal", result.Workspace.Name);
        Assert.Equal(TimeSpan.Zero, result.Workspace.CreatedAtUtc.Offset);
    }

    [Fact]
    public async Task GetAsync_RetrievesWorkspace()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri($"{BaseUrl}/api/workspaces/{WorkspaceId:D}"),
                request.RequestUri);
            return Task.FromResult(Response(HttpStatusCode.OK,
                "WORKSPACE_RETRIEVED", true,
                new { id = WorkspaceId, name = "Personal", createdAtUtc = "2026-11-05T21:00:00+00:00" }));
        });

        var result = await CreateClient(http).GetAsync(WorkspaceId.ToString());

        Assert.Equal(WorkspaceOperationState.Success, result.State);
        Assert.Equal("Personal", result.Workspace!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public async Task CreateAsync_RejectsEmptyNameWithoutHttp(string? name)
    {
        using var http = RejectUnexpectedHttp();
        var result = await CreateClient(http).CreateAsync(name);
        Assert.Equal(WorkspaceOperationState.Invalid, result.State);
    }

    [Fact]
    public async Task CreateAsync_RejectsLongNameWithoutHttp()
    {
        using var http = RejectUnexpectedHttp();
        var result = await CreateClient(http).CreateAsync(new string('x', 101));
        Assert.Equal(WorkspaceOperationState.Invalid, result.State);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetAsync_RejectsInvalidIdWithoutHttp(string id)
    {
        using var http = RejectUnexpectedHttp();
        var result = await CreateClient(http).GetAsync(id);
        Assert.Equal(WorkspaceOperationState.Invalid, result.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "WORKSPACE_NAME_CONFLICT",
        WorkspaceOperationState.Conflict)]
    [InlineData(HttpStatusCode.Forbidden, "ACCESS_DENIED",
        WorkspaceOperationState.Denied)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE",
        WorkspaceOperationState.Unavailable)]
    public async Task CreateAsync_MapsApiFailures(
        HttpStatusCode status,
        string code,
        WorkspaceOperationState expected)
    {
        using var http = CreateHttpClient((_, _) =>
            Task.FromResult(Response(status, code)));
        var result = await CreateClient(http).CreateAsync("Personal");
        Assert.Equal(expected, result.State);
    }

    [Fact]
    public async Task GetAsync_SeparatesMissingWorkspaceFromDisabledApi()
    {
        using var missing = CreateHttpClient((_, _) =>
            Task.FromResult(Response(HttpStatusCode.NotFound, "WORKSPACE_NOT_FOUND")));
        using var disabled = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var notFound = await CreateClient(missing).GetAsync(WorkspaceId.ToString());
        var noRoute = await CreateClient(disabled).GetAsync(WorkspaceId.ToString());

        Assert.Equal(WorkspaceOperationState.NotFound, notFound.State);
        Assert.Equal(WorkspaceOperationState.Disabled, noRoute.State);
    }

    [Fact]
    public async Task GetAsync_ReportsNetworkFailure()
    {
        using var http = CreateHttpClient((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        var result = await CreateClient(http).GetAsync(WorkspaceId.ToString());
        Assert.Equal(WorkspaceOperationState.Unavailable, result.State);
    }

    [Fact]
    public async Task GetAsync_ReportsTimeout()
    {
        using var http = CreateHttpClient(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new WorkspaceApiClient(http, BaseUrl, TimeSpan.FromMilliseconds(50));
        var result = await client.GetAsync(WorkspaceId.ToString());
        Assert.Equal(WorkspaceOperationState.Unavailable, result.State);
    }

    [Theory]
    [InlineData("invalid-url")]
    [InlineData("http://example.com")]
    public async Task GetAsync_RejectsInvalidOrInsecureConfiguration(string url)
    {
        using var http = RejectUnexpectedHttp();
        var client = new WorkspaceApiClient(http, url, TimeSpan.FromSeconds(2));
        var result = await client.GetAsync(WorkspaceId.ToString());
        Assert.Equal(WorkspaceOperationState.ConfigurationError, result.State);
    }

    private static WorkspaceApiClient CreateClient(HttpClient http) =>
        new(http, BaseUrl, TimeSpan.FromSeconds(5));

    private static HttpClient RejectUnexpectedHttp() => CreateHttpClient((_, _) =>
        throw new InvalidOperationException("Unexpected HTTP call."));

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send));

    private static HttpResponseMessage Response(
        HttpStatusCode status,
        string code,
        bool success = false,
        object? data = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            success,
            status = (int)status,
            code,
            message = "Test response",
            data,
            errors = Array.Empty<string>(),
            traceId = "test-trace",
            timestamp = "2026-11-05T21:00:00+00:00"
        });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
