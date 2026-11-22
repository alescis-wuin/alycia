using Alycia.Modules.Chat.Application;

namespace Alycia.Modules.Chat.Tests;

public sealed class StreamingTests
{
    [Fact]
    public async Task Optional_turn_scope_observes_cancellation_before_start()
    {
        IChatRepository repository = new ConversationTestRepository(0);
        var token = new CancellationToken(true);
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.BeginTurnAsync(Guid.Empty, token));
        Assert.Equal(token, error.CancellationToken);
        Assert.Null(await repository.BeginTurnAsync(Guid.Empty, default));
    }
}
