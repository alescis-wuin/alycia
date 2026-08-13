using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppServerSecurity
{
    public const string ApiKeyEnvironmentVariable = "LLAMA_API_KEY";

    private const int ApiKeyByteLength = 32;

    public static string CreateEphemeralApiKey()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(ApiKeyByteLength));
    }

    public static void ApplyApiKey(
        ProcessStartInfo startInfo,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        startInfo.Environment[ApiKeyEnvironmentVariable] = apiKey;
    }

    public static void Authorize(
        HttpRequestMessage request,
        string apiKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}
