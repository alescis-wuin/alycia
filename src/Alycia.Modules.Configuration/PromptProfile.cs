namespace Alycia.Modules.Configuration;

public sealed class PromptProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
}
