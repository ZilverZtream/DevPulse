using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class DevOpsEvent
{
    public string EventId { get; set; } = string.Empty;
    public DevOpsEventType EventType { get; set; }
    public PrEventSource EventSource { get; set; }
    public EventMeaning EventMeaning { get; set; }
    public string InboxName { get; set; } = string.Empty;
    public bool IsCollapsed { get; set; }
    public int CollapsedCount { get; set; } = 1;
    public int PullRequestId { get; set; }
    public string PullRequestTitle { get; set; } = string.Empty;
    public string PullRequestUrl { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string AuthorDisplayName { get; set; } = string.Empty;
    public string AuthorCanonicalKey { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset DiscoveredAtUtc { get; set; }
    public string SourceThreadId { get; set; } = string.Empty;
    public string SourceCommentId { get; set; } = string.Empty;
    public string? LinkedWorkItemId { get; set; }
    public bool NotificationSent { get; set; }
    public bool IsRead { get; set; }
    public string? MatchedRuleDescription { get; set; }
    public bool IsCurrentUserReviewer { get; set; }
}
