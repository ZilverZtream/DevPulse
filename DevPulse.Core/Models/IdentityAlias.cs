namespace DevPulse.Core.Models;

public sealed class IdentityAlias
{
    public string CanonicalKey { get; set; } = string.Empty;
    public List<string> Variants { get; set; } = [];
}
