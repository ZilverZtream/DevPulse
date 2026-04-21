namespace DevPulse.Core.Models;

public sealed class AiValidationResult
{
    public bool IsValid { get; set; }
    public List<string> MissingHeaders { get; set; } = [];
    public List<string> EmptySections { get; set; } = [];
}
