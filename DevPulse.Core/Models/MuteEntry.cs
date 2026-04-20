using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class MuteEntry
{
    public MuteScope Scope { get; set; }
    public string Key { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}
