using DevPulse.Core.Enums;
using System.Text.Json.Serialization;

namespace DevPulse.Core.Models;

public sealed class Watcher
{
    public WatcherType Type { get; set; }
    [JsonPropertyName("pattern")]
    public string MatchValue { get; set; } = string.Empty;
    public string TargetInbox { get; set; } = string.Empty;
}
