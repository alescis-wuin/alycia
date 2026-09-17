using Alicia.Application.Conversations;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers;

public sealed class InferenceProviderRegistry :
    IInferenceProviderRegistry,
    IStreamingConversationResponder
{
    private readonly Dictionary<string, InferenceProviderRegistration> _registrations;

    public InferenceProviderRegistry(IEnumerable<InferenceProviderRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        Dictionary<string, InferenceProviderRegistration> mapped = new(StringComparer.Ordinal);

        foreach (InferenceProviderRegistration registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);

            if (!mapped.TryAdd(registration.Descriptor.Id, registration))
            {
                throw new ArgumentException(
                    $"Provider identifier '{registration.Descriptor.Id}' is registered more than once.",
                    nameof(registrations));
            }
        }

        if (mapped.Count == 0)
        {
            throw new ArgumentException(
                "At least one inference provider must be registered.",
                nameof(registrations));
        }

        _registrations = mapped;
        Providers = mapped.Values
            .Select(registration => registration.Descriptor)
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<InferenceProviderDescriptor> Providers { get; }

    public string? SelectedProviderId { get; private set; }

    public void SelectProvider(string providerId)
    {
        string normalizedId = NormalizeProviderId(providerId);

        if (!_registrations.ContainsKey(normalizedId))
        {
            throw new KeyNotFoundException(
                $"Inference provider '{normalizedId}' is not registered.");
        }

        SelectedProviderId = normalizedId;
    }

    public IInferenceProviderRuntime GetRequiredRuntime(string providerId)
    {
        string normalizedId = NormalizeProviderId(providerId);

        if (!_registrations.TryGetValue(normalizedId, out InferenceProviderRegistration? registration))
        {
            throw new KeyNotFoundException(
                $"Inference provider '{normalizedId}' is not registered.");
        }

        return registration.Runtime;
    }

    public IAsyncEnumerable<ConversationResponseChunk> StreamAsync(
        ConversationResponseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? requestedProviderId = request.GenerationSnapshot?.ProviderId;
        string providerId = requestedProviderId
            ?? SelectedProviderId
            ?? throw new InvalidOperationException(
                "No inference provider is selected. Save provider settings before starting a conversation response.");

        if (!_registrations.TryGetValue(
            providerId,
            out InferenceProviderRegistration? registration))
        {
            throw new InvalidOperationException(
                $"Generation request references unregistered provider '{providerId}'.");
        }

        return registration.StreamingResponder
            .StreamAsync(request, cancellationToken);
    }

    private static string NormalizeProviderId(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "Provider identifier cannot be empty.",
                nameof(providerId));
        }

        return providerId.Trim();
    }
}
