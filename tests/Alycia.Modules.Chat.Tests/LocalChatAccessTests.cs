using System.Net;
using Alycia.Api.Chat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Alycia.Modules.Chat.Tests;

public sealed class LocalChatAccessTests
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static LocalChatAccess Access(bool container = false, string? token = Token) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ALYCIA_CHAT_CONTAINER_MODE"] = container.ToString(),
            ["ALYCIA_CHAT_ACCESS_TOKEN"] = token
        }).Build());

    [Fact]
    public void DefaultModeStillRequiresActualLoopbackPeer()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.1");
        http.Request.Headers.Authorization = "Bearer " + Token;
        http.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        Assert.False(Access().IsAllowed(http));
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        Assert.True(Access().IsAllowed(http));
        http.Request.Headers.Origin = "http://localhost";
        Assert.False(Access().IsAllowed(http));
    }

    [Fact]
    public void ContainerModeRequiresTokenEvenFromLoopbackAndRejectsBrowserOrigin()
    {
        var access = Access(true);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        Assert.False(access.IsAllowed(http));
        http.Request.Headers.Authorization = "Bearer " + new string('f', 64);
        Assert.False(access.IsAllowed(http));
        http.Request.Headers.Authorization = "Bearer " + Token;
        http.Connection.RemoteIpAddress = IPAddress.Parse("172.18.0.1");
        Assert.True(access.IsAllowed(http));
        http.Request.Headers.Origin = "null";
        Assert.False(access.IsAllowed(http));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void ContainerModeFailsClosedWithoutConfiguredSecret(string? token) =>
        Assert.Throws<InvalidOperationException>(() => Access(true, token));

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://ollama.example.com:11434/")]
    [InlineData("http://ollama:11434/path")]
    [InlineData("http://user:password@ollama:11434/")]
    public void RemoteOrAmbiguousProviderOriginsAreRejected(string url) =>
        Assert.Throws<InvalidOperationException>(() => Access(true).ValidateOllamaUrl(url));

    [Fact]
    public void ComposeServiceOriginRequiresAuthenticatedContainerMode()
    {
        Assert.Throws<InvalidOperationException>(() => Access().ValidateOllamaUrl("http://ollama:11434/"));
        Assert.Equal("ollama", Access(true).ValidateOllamaUrl("http://ollama:11434/").Host);
        Assert.True(Access().ValidateOllamaUrl("http://127.0.0.1:11434/").IsLoopback);
    }
}
