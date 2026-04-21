namespace DevPulse.Core.Models;

public sealed class AiTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> AppliesTo { get; set; } = [];
    public List<string> RequiredHeaders { get; set; } = [];
    public string PromptBody { get; set; } = string.Empty;
}
