using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class Watcher
{
    public WatcherType Type { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public string TargetInbox { get; set; } = string.Empty;
}
