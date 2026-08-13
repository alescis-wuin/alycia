using System.Net;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppServerSessionProbe
{
    public static async Task VerifyOwnershipAsync(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        Uri propsUri = new(endpoint, "props");

        using (HttpRequestMessage unauthenticatedRequest = new(HttpMethod.Get, propsUri))
        using (HttpResponseMessage unauthenticatedResponse = await httpClient
            .SendAsync(
                unauthenticatedRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false))
        {
            if (unauthenticatedResponse.StatusCode != HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(
                    "The selected llama-server endpoint does not enforce the Alicia session credential.");
            }
        }

        using HttpRequestMessage authenticatedRequest = new(HttpMethod.Get, propsUri);
        LlamaCppServerSecurity.Authorize(authenticatedRequest, apiKey);
        using HttpResponseMessage authenticatedResponse = await httpClient
            .SendAsync(
                authenticatedRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (authenticatedResponse.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"The selected llama-server endpoint rejected the Alicia session credential with HTTP {(int)authenticatedResponse.StatusCode} ({authenticatedResponse.StatusCode}).");
        }
    }
}
