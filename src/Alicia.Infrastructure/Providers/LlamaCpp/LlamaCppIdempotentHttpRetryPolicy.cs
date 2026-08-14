using System.Net;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed class LlamaCppIdempotentHttpRetryPolicy
{
    public const int DefaultMaxAttempts = 3;

    private static readonly TimeSpan _defaultRetryDelay = TimeSpan.FromMilliseconds(150);

    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public LlamaCppIdempotentHttpRetryPolicy(
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? retryDelay = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAttempts),
                "Retry attempts must be at least one.");
        }

        TimeSpan resolvedDelay = retryDelay ?? _defaultRetryDelay;

        if (resolvedDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelay),
                "Retry delay cannot be negative.");
        }

        _maxAttempts = maxAttempts;
        _retryDelay = resolvedDelay;
    }

    public static LlamaCppIdempotentHttpRetryPolicy Default { get; } = new();

    public async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendOnce,
        string userMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sendOnce);
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                HttpResponseMessage response = await sendOnce(cancellationToken)
                    .ConfigureAwait(false);

                if (!IsTransientStatusCode(response.StatusCode))
                {
                    return response;
                }

                HttpStatusCode statusCode = response.StatusCode;
                response.Dispose();

                if (attempt == _maxAttempts)
                {
                    throw new InferenceProviderException(
                        InferenceProviderFailureKind.Network,
                        userMessage,
                        new HttpRequestException(
                            $"Idempotent HTTP request exhausted {_maxAttempts} attempt(s) with HTTP {(int)statusCode} ({statusCode}).",
                            inner: null,
                            statusCode));
                }
            }
            catch (InferenceProviderException exception)
                when (exception.Kind == InferenceProviderFailureKind.Network)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt == _maxAttempts)
                {
                    throw;
                }
            }
            catch (HttpRequestException exception)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt == _maxAttempts)
                {
                    throw new InferenceProviderException(
                        InferenceProviderFailureKind.Network,
                        userMessage,
                        exception);
                }
            }

            if (_retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Retry policy exhausted without returning or throwing.");
    }

    internal static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }
}
