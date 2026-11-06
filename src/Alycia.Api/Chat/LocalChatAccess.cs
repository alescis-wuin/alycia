using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Alycia.Api.Chat;

// Docker Desktop forwards loopback-published ports through a bridge. In this
// explicit mode, authenticate the native client instead of trusting that peer.
public sealed class LocalChatAccess
{
    private readonly bool _containerMode;
    private readonly byte[]? _tokenHash;

    public LocalChatAccess(IConfiguration configuration)
    {
        _containerMode = configuration.GetValue<bool>("ALYCIA_CHAT_CONTAINER_MODE");
        if (!_containerMode) return;
        var token = configuration["ALYCIA_CHAT_ACCESS_TOKEN"];
        if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Container chat requires a random 32-byte hex ALYCIA_CHAT_ACCESS_TOKEN.");
        _tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public bool IsAllowed(HttpContext context)
    {
        if (context.Request.Headers.ContainsKey("Origin")) return false;
        if (!_containerMode)
            return context.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
        if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var auth)
            || !auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            || auth.Parameter is not { Length: 64 } token) return false;
        return CryptographicOperations.FixedTimeEquals(_tokenHash!, SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    public Uri ValidateOllamaUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !(uri.IsLoopback || _containerMode && uri.Host == "ollama" && uri.Port == 11434 && uri.Scheme == "http")
            || uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/"
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidOperationException("Ollama must use a loopback origin, or http://ollama:11434 in authenticated container mode.");
        return uri;
    }
}
