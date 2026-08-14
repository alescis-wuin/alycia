using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed record LlamaCppProviderTimeouts
{
    public static LlamaCppProviderTimeouts Default { get; } = new();

    public LlamaCppProviderTimeouts(
        TimeSpan? readiness = null,
        TimeSpan? healthRequest = null,
        TimeSpan? commandProbe = null,
        TimeSpan? chatResponseHeaders = null,
        TimeSpan? chatStreamIdle = null,
        TimeSpan? installerResponseHeaders = null,
        TimeSpan? installerReadIdle = null)
    {
        Readiness = Validate(readiness ?? TimeSpan.FromMinutes(2), nameof(readiness));
        HealthRequest = Validate(healthRequest ?? TimeSpan.FromSeconds(5), nameof(healthRequest));
        CommandProbe = Validate(commandProbe ?? TimeSpan.FromSeconds(15), nameof(commandProbe));
        ChatResponseHeaders = Validate(
            chatResponseHeaders ?? TimeSpan.FromSeconds(30),
            nameof(chatResponseHeaders));
        ChatStreamIdle = Validate(
            chatStreamIdle ?? TimeSpan.FromSeconds(30),
            nameof(chatStreamIdle));
        InstallerResponseHeaders = Validate(
            installerResponseHeaders ?? TimeSpan.FromSeconds(30),
            nameof(installerResponseHeaders));
        InstallerReadIdle = Validate(
            installerReadIdle ?? TimeSpan.FromSeconds(30),
            nameof(installerReadIdle));
    }

    public TimeSpan Readiness { get; }

    public TimeSpan HealthRequest { get; }

    public TimeSpan CommandProbe { get; }

    public TimeSpan ChatResponseHeaders { get; }

    public TimeSpan ChatStreamIdle { get; }

    public TimeSpan InstallerResponseHeaders { get; }

    public TimeSpan InstallerReadIdle { get; }

    private static TimeSpan Validate(TimeSpan value, string parameterName)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Provider timeouts must be finite and greater than zero.");
        }

        return value;
    }
}

internal static class LlamaCppTimeoutGuard
{
    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        InferenceProviderFailureKind failureKind,
        string userMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await operation(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new InferenceProviderException(
                failureKind,
                userMessage,
                new TimeoutException(
                    $"The provider operation exceeded its {timeout.TotalMilliseconds:0} ms timeout.",
                    exception));
        }
    }
}
