using BuildMode = Alycia.Contracts.Context.TurnContextMode;

namespace Alycia.Modules.Chat.Application.Context;

// Public conversation command preserved for callers and serialized test cases.
public enum TurnContextMode { Send, Preview, PrepareMemory }

public static class ConversationContextModes
{
    public static BuildMode ForContext(this TurnContextMode mode) => mode switch
    {
        TurnContextMode.Send => BuildMode.Send,
        TurnContextMode.Preview => BuildMode.Preview,
        TurnContextMode.PrepareMemory => BuildMode.PrepareMemory,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
