namespace DevPulse.Core.Models;

public sealed record AiGenerateRequest(string Prompt, string Model, TimeSpan Timeout);
