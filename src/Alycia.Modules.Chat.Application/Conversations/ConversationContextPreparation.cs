using Alycia.Contracts.Context;
using Alycia.Contracts.Tools;
using Alycia.Modules.Chat.Domain;

using Alycia.Modules.Chat.Application.Context;

namespace Alycia.Modules.Chat.Application.Conversations;

public sealed record ContextPreparationResult(ContextWindow Context, ChatOptions Options, GenerationUsage Usage, int Revision, int OmittedMessages, string Estimator);

public sealed class ConversationContextPreparation(IChatRepository repository, IChatModel model, ITurnTools tools, ITurnContext turnContext,
    IConversationMemory memoryWriter, IProjectRetrieval retrieval, ITemporalMemory memory, ChatOptions defaults, GenerationGate gate)
{
    private sealed record Inputs(Conversation Conversation, ContextConversation ContextOwner, string Text, ChatOptions Options, MeteredChatModel Measured, TurnContextBudget Budget);

    public async Task<ContextPreparationResult> PreviewAsync(Guid id, string? content, CancellationToken ct)
    {
        var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
        var options = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), ct);
        var text = Conversation.RequiredText(content, 32000, "Le message");
        var inputs = await RouteAsync(conversation, text, options, ct);
        return await BuildAsync(inputs, TurnContextMode.Preview, null, ct);
    }

    public async Task<PendingPreparation> BeginAsync(Guid id, int revision, string? content, CancellationToken ct)
    {
        var lease = gate.Enter(id);
        try
        {
            var conversation = await repository.FindAsync(id, ct) ?? throw new ConversationNotFoundException();
            ConversationConfiguration.CheckRevision(conversation, revision);
            var text = Conversation.RequiredText(content, 32000, "La demande");
            var options = await model.PrepareOptionsAsync(conversation.Model, ConversationConfiguration.Options(conversation, defaults), ct);
            var inputs = await RouteAsync(conversation, text, options, ct);
            return new(lease, progress => BuildAsync(inputs, TurnContextMode.PrepareMemory, progress, ct));
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private async Task<Inputs> RouteAsync(Conversation conversation, string text, ChatOptions options, CancellationToken ct)
    {
        var measured = new MeteredChatModel(model);
        await QueryRouting.PrepareAsync(repository, conversation.Id, conversation.MessageCount, text, options, retrieval, measured, ct, memory);
        var useTools = await tools.AvailableForContextAsync(new ToolTurnScope(conversation.Id, conversation.WorkspaceId, conversation.Model),
            options.UseProjectTools, model, ct);
        var contextOwner = conversation.ForContext(options);
        var budget = await turnContext.AllocateAsync(contextOwner, text, options, useTools, ct);
        return new(conversation, contextOwner, text, options, measured, budget);
    }

    private async Task<ContextPreparationResult> BuildAsync(Inputs inputs, TurnContextMode mode, Func<string, Task>? progress, CancellationToken ct)
    {
        var result = await turnContext.BuildAsync(inputs.ContextOwner, inputs.Text, inputs.Budget, model, inputs.Measured, mode, progress, ct);
        if (mode == TurnContextMode.PrepareMemory && memoryWriter.Apply(inputs.Conversation, result.Prepared?.MemoryChange()))
            await repository.SaveAsync(ct);
        return new(result.Window, inputs.Options, inputs.Measured.Snapshot(inputs.Options), inputs.Conversation.Revision,
            inputs.Conversation.MessageCount - result.Window.HistoryMessagesIncluded, TokenBudget.Description(inputs.Options));
    }

    // The caller holds the conversation lease through its final write, including a failed or cancelled stream.
    public sealed class PendingPreparation : IDisposable
    {
        private readonly IDisposable _lease;
        private Func<Func<string, Task>, Task<ContextPreparationResult>>? _prepare;

        internal PendingPreparation(IDisposable lease, Func<Func<string, Task>, Task<ContextPreparationResult>> prepare)
        {
            _lease = lease;
            _prepare = prepare;
        }

        public Task<ContextPreparationResult> BuildAsync(Func<string, Task> progress)
        {
            ObjectDisposedException.ThrowIf(_prepare is null, this);
            return _prepare(progress);
        }

        public void Dispose()
        {
            _prepare = null;
            _lease.Dispose();
        }
    }
}
