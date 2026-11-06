using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alycia.Client.Avalonia;

namespace Alycia.Client.Avalonia.Tests;

public sealed class OwnerBootstrapApiClientTests
{
    private const string BaseUrl = "http://127.0.0.1:5187";
    private const string Secret = "LOCAL-TEST-SECRET";
    private const string AllowedPassword = "correct horse battery staple";
    private static readonly Guid OwnerId =
        Guid.Parse("aa30adba-52bf-4a38-bb8f-7167630c93ae");

    [Theory]
    [InlineData(false, OwnerSetupState.Ready)]
    [InlineData(true, OwnerSetupState.Initialized)]
    public async Task StatusAsync_MapsOwnerState(bool initialized, OwnerSetupState expected)
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(new Uri($"{BaseUrl}/api/identity/bootstrap/status"), request.RequestUri);
            return Task.FromResult(Response(HttpStatusCode.OK, "BOOTSTRAP_STATUS", true,
                new { initialized }));
        });

        var result = await Client(http).CheckStatusAsync();

        Assert.Equal(expected, result.State);
    }

    [Fact]
    public async Task InitializeAsync_SendsFourFieldsAndNormalizesOnlyUsername()
    {
        using var http = CreateHttpClient(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri($"{BaseUrl}/api/identity/bootstrap"), request.RequestUri);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var data = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            var body = data.RootElement;
            Assert.Equal(Secret, body.GetProperty("secret").GetString());
            Assert.Equal("LOCAL.OWNER", body.GetProperty("loginIdentifier").GetString());
            Assert.Equal(AllowedPassword, body.GetProperty("password").GetString());
            Assert.Equal(AllowedPassword, body.GetProperty("passwordConfirmation").GetString());
            return Response(HttpStatusCode.Created, "OWNER_INITIALIZED", true,
                new { ownerId = OwnerId });
        });

        var result = await Client(http).InitializeAsync(
            Secret, " local.owner ", AllowedPassword, AllowedPassword);

        Assert.Equal(OwnerSetupState.Success, result.State);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.DoesNotContain(AllowedPassword, result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InitializeAsync_RejectsMissingSecretBeforeHttp(string? secret)
    {
        using var http = RejectUnexpectedHttp();
        var result = await Client(http).InitializeAsync(
            secret, "owner", AllowedPassword, AllowedPassword);
        Assert.Equal(OwnerSetupState.InvalidInput, result.State);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("invalid name")]
    [InlineData("über")]
    public async Task InitializeAsync_RejectsInvalidUsernameBeforeHttp(string login)
    {
        using var http = RejectUnexpectedHttp();
        var result = await Client(http).InitializeAsync(
            Secret, login, AllowedPassword, AllowedPassword);
        Assert.Equal(OwnerSetupState.InvalidInput, result.State);
    }

    [Theory]
    [InlineData(14, OwnerSetupState.InvalidPassword)]
    [InlineData(15, OwnerSetupState.Success)]
    [InlineData(128, OwnerSetupState.Success)]
    [InlineData(129, OwnerSetupState.InvalidPassword)]
    public async Task InitializeAsync_EnforcesUnicodeCodePointBounds(
        int count, OwnerSetupState expected)
    {
        var password = string.Concat(System.Linq.Enumerable.Repeat("🚀", count));
        using var http = CreateHttpClient((_, _) =>
            Task.FromResult(Response(HttpStatusCode.Created, "OWNER_INITIALIZED", true,
                new { ownerId = OwnerId })));

        var result = await Client(http).InitializeAsync(Secret, "owner", password, password);

        Assert.Equal(expected, result.State);
    }

    [Fact]
    public async Task InitializeAsync_AcceptsNfcEquivalentConfirmationWithoutTruncatingSpaces()
    {
        var password = "Cafe\u0301  " + new string('x', 12);
        var confirmation = "Café  " + new string('x', 12);
        using var http = CreateHttpClient(async (request, ct) =>
        {
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(password, data.RootElement.GetProperty("password").GetString());
            Assert.Equal(confirmation, data.RootElement.GetProperty("passwordConfirmation").GetString());
            return Response(HttpStatusCode.Created, "OWNER_INITIALIZED", true,
                new { ownerId = OwnerId });
        });

        var result = await Client(http).InitializeAsync(
            Secret, "owner", password, confirmation);

        Assert.Equal(OwnerSetupState.Success, result.State);
    }

    [Fact]
    public async Task InitializeAsync_RejectsConfirmationMismatchBeforeHttp()
    {
        using var http = RejectUnexpectedHttp();
        var result = await Client(http).InitializeAsync(
            Secret, "owner", AllowedPassword, AllowedPassword + "!");
        Assert.Equal(OwnerSetupState.ConfirmationMismatch, result.State);
    }

    [Fact]
    public async Task InitializeAsync_RejectsControlCharactersBeforeHttp()
    {
        var password = "twelve chars\rmore";
        using var http = RejectUnexpectedHttp();
        var result = await Client(http).InitializeAsync(Secret, "owner", password, password);
        Assert.Equal(OwnerSetupState.InvalidPassword, result.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_PASSWORD", OwnerSetupState.InvalidPassword)]
    [InlineData(HttpStatusCode.BadRequest, "INVALID_LOGIN_IDENTIFIER", OwnerSetupState.InvalidInput)]
    [InlineData(HttpStatusCode.BadRequest, "PASSWORD_CONFIRMATION_MISMATCH", OwnerSetupState.ConfirmationMismatch)]
    [InlineData(HttpStatusCode.Unauthorized, "INVALID_BOOTSTRAP_SECRET", OwnerSetupState.InvalidSecret)]
    [InlineData(HttpStatusCode.Conflict, "BOOTSTRAP_NOT_READY", OwnerSetupState.NotReady)]
    [InlineData(HttpStatusCode.Conflict, "OWNER_ALREADY_INITIALIZED", OwnerSetupState.AlreadyInitialized)]
    [InlineData(HttpStatusCode.Gone, "BOOTSTRAP_EXPIRED", OwnerSetupState.Expired)]
    [InlineData(HttpStatusCode.TooManyRequests, "BOOTSTRAP_LOCKED", OwnerSetupState.Locked)]
    [InlineData(HttpStatusCode.Forbidden, "LOCAL_ONLY", OwnerSetupState.Denied)]
    [InlineData(HttpStatusCode.NotFound, "NOT_FOUND", OwnerSetupState.Disabled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE", OwnerSetupState.Unavailable)]
    public async Task InitializeAsync_MapsSafeErrorStates(
        HttpStatusCode code, string serverCode, OwnerSetupState expected)
    {
        using var http = CreateHttpClient((_, _) =>
            Task.FromResult(Response(code, serverCode)));
        var result = await Client(http).InitializeAsync(
            Secret, "owner", AllowedPassword, AllowedPassword);

        Assert.Equal(expected, result.State);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.DoesNotContain(AllowedPassword, result.Message);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("http://127.0.0.1:5187?token=bad")]
    [InlineData("http://127.0.0.1:5187/path")]
    [InlineData("http://user:password@127.0.0.1:5187")]
    [InlineData("not-a-url")]
    public async Task InitializeAsync_RejectsNonlocalOrAmbiguousUrl(string url)
    {
        using var http = RejectUnexpectedHttp();
        var client = new OwnerBootstrapApiClient(http, url, TimeSpan.FromSeconds(1));
        var result = await client.InitializeAsync(
            Secret, "owner", AllowedPassword, AllowedPassword);
        Assert.Equal(OwnerSetupState.ConfigurationError, result.State);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotTreatRedirectAsSuccess()
    {
        using var http = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.com/") }
            }));
        var result = await Client(http).InitializeAsync(
            Secret, "owner", AllowedPassword, AllowedPassword);
        Assert.Equal(OwnerSetupState.Error, result.State);
    }

    [Fact]
    public async Task StatusAsync_ReportsNetworkError()
    {
        using var http = CreateHttpClient((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("offline")));
        var result = await Client(http).CheckStatusAsync();
        Assert.Equal(OwnerSetupState.Unavailable, result.State);
    }

    [Fact]
    public async Task StatusAsync_ReportsTimeout()
    {
        using var http = CreateHttpClient(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new OwnerBootstrapApiClient(http, BaseUrl, TimeSpan.FromMilliseconds(50));
        var result = await client.CheckStatusAsync();
        Assert.Equal(OwnerSetupState.Unavailable, result.State);
    }

    [Fact]
    public async Task StatusAsync_RejectsMalformedSuccessEnvelope()
    {
        using var http = CreateHttpClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
            }));
        var result = await Client(http).CheckStatusAsync();
        Assert.Equal(OwnerSetupState.Error, result.State);
    }

    private static OwnerBootstrapApiClient Client(HttpClient http) =>
        new(http, BaseUrl, TimeSpan.FromSeconds(5));

    private static HttpClient RejectUnexpectedHttp() => CreateHttpClient((_, _) =>
        throw new InvalidOperationException("Unexpected HTTP call."));

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send));

    private static HttpResponseMessage Response(
        HttpStatusCode status, string code, bool success = false, object? data = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            success,
            status = (int)status,
            code,
            message = "Test response",
            data,
            errors = Array.Empty<string>(),
            traceId = "test-trace",
            timestamp = "2026-11-06T00:00:00+00:00"
        });
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
