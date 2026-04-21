using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class AiAttempt
{
    public string Id { get; set; } = string.Empty;
    public int WorkItemId { get; set; }
    public string Project { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public AiAttemptStatus Status { get; set; }
    public bool ValidationPassed { get; set; }
    public List<string> MissingSections { get; set; } = [];
    public string SpecFilePath { get; set; } = string.Empty;
    public string PromptFilePath { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
