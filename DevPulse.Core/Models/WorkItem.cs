using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class WorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public WorkItemType Type { get; set; }
    public string State { get; set; } = string.Empty;
    public string BoardColumn { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string AssignedToDisplayName { get; set; } = string.Empty;
    public string AssignedToCanonicalKey { get; set; } = string.Empty;
    public string AreaPath { get; set; } = string.Empty;
    public string IterationPath { get; set; } = string.Empty;
    public string WorkItemUrl { get; set; } = string.Empty;
    public string? LinkedPullRequestId { get; set; }
    public DateTimeOffset StateChangedAtUtc { get; set; }
    public int DaysInCurrentState { get; set; }
    public AgingLevel AgingLevel { get; set; }
    public DateTimeOffset DiscoveredAtUtc { get; set; }
    public DateTimeOffset? FirstSeenUtc { get; set; }
}
