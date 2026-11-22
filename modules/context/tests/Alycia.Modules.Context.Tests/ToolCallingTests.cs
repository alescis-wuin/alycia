using Alycia.Contracts.Chat;
using Alycia.Contracts.Context;
using Alycia.Modules.Context;

namespace Alycia.Modules.Chat.Tests;

public sealed class ToolCallingTests
{
    [Fact]
    public void DefaultWindowReservesAllNineToolsAndStillFitsTheProtectedQuestion()
    {
        var options = TurnContextBudget.WithToolReserve(new ChatSettings().Context());
        var context = SlidingContext.Build(new Alycia.Modules.Context.SlidingHistory(), "Réponds brièvement en français.", [], "Cherche le symbole ReadAsync puis lis son implémentation.", options);
        Assert.True(context.EstimatedInputTokens + options.MaxOutputTokens + options.SafetyTokens <= options.ContextTokens);
    }
}
