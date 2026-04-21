namespace DevPulse.Core.Models;

public sealed class AiProviderProfile
{
    public string ProviderId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DefaultModel { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 90;
}
