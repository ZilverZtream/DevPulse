namespace DevPulse.Core.Models;

public sealed class InboxDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsSystemInbox { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public int MaxItemsToRetain { get; set; } = 100;
    public List<InboxRule> Rules { get; set; } = [];
}
