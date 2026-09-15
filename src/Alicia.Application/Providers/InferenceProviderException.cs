using System.Diagnostics.CodeAnalysis;

namespace Alicia.Application.Providers;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Provider failures require an explicit classification and user-safe message.")]
public sealed class InferenceProviderException : Exception
{
    public InferenceProviderException(
        InferenceProviderFailureKind kind,
        string userMessage,
        Exception? innerException = null)
        : base(NormalizeUserMessage(userMessage), innerException)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
    }

    public InferenceProviderFailureKind Kind { get; }

    public string UserMessage => Message;

    private static string NormalizeUserMessage(string userMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        return userMessage.Trim();
    }
}
