namespace DevPulse.Core.Models;

public sealed record AiGenerateResult(
    string Markdown,
    string ModelUsed,
    int TokensIn,
    int TokensOut,
    TimeSpan Duration,
    string? ErrorMessage);
