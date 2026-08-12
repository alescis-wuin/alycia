using Alicia.Application.Conversations;
using Alicia.Application.Providers;

namespace Alicia.Infrastructure.Providers;

public sealed record InferenceProviderRegistration
{
    public InferenceProviderRegistration(
        InferenceProviderDescriptor descriptor,
        IInferenceProviderRuntime runtime,
        IStreamingConversationResponder streamingResponder)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(streamingResponder);

        Descriptor = descriptor;
        Runtime = runtime;
        StreamingResponder = streamingResponder;
    }

    public InferenceProviderDescriptor Descriptor { get; }

    public IInferenceProviderRuntime Runtime { get; }

    public IStreamingConversationResponder StreamingResponder { get; }
}
