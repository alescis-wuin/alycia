using System.Net;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppServerSessionProbe
{
    public static Task VerifyOwnershipAsync(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        return VerifyOwnershipAsync(
            httpClient,
            endpoint,
            apiKey,
            LlamaCppIdempotentHttpRetryPolicy.Default,
            cancellationToken);
    }

    internal static async Task VerifyOwnershipAsync(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        LlamaCppIdempotentHttpRetryPolicy retryPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        Uri propsUri = new(endpoint, "props");

        using HttpResponseMessage unauthenticatedResponse = await retryPolicy.SendAsync(
            async token =>
            {
                using HttpRequestMessage request = new(HttpMethod.Get, propsUri);
                return await httpClient
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        token)
                    .ConfigureAwait(false);
            },
            "The local AI server did not respond reliably during its session check. Restart the provider and try again.",
            cancellationToken).ConfigureAwait(false);

        if (unauthenticatedResponse.StatusCode != HttpStatusCode.Unauthorized)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Faulted,
                "The local AI server failed its session security check. Restart the provider and try again.",
                new InvalidOperationException(
                    "The selected llama-server endpoint does not enforce the Alicia session credential."));
        }

        using HttpResponseMessage authenticatedResponse = await retryPolicy.SendAsync(
            async token =>
            {
                using HttpRequestMessage request = new(HttpMethod.Get, propsUri);
                LlamaCppServerSecurity.Authorize(request, apiKey);
                return await httpClient
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        token)
                    .ConfigureAwait(false);
            },
            "The local AI server did not respond reliably during its authenticated session check. Restart the provider and try again.",
            cancellationToken).ConfigureAwait(false);

        if (authenticatedResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InferenceProviderException(
                InferenceProviderFailureKind.Faulted,
                "The local AI server rejected its Alicia session credential. Restart the provider and try again.",
                new InvalidOperationException(
                    $"The selected llama-server endpoint rejected the Alicia session credential with HTTP {(int)authenticatedResponse.StatusCode} ({authenticatedResponse.StatusCode})."));
        }
    }
}
