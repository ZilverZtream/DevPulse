using DevPulse.Core.Enums;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class EventCollapser
{
    private readonly EventNormalizer _normalizer = new();

    /// <summary>
    /// Collapses related events within a poll cycle. Bot comments on the same PR become one
    /// grouped row. System events on the same PR within a 5-minute window are also merged.
    /// </summary>
    public IReadOnlyList<DevOpsEvent> Collapse(IReadOnlyList<DevOpsEvent> events, DateTimeOffset pollTime)
    {
        var result = new List<DevOpsEvent>();
        var botGroups = new Dictionary<int, List<DevOpsEvent>>();
        var systemGroups = new Dictionary<int, List<DevOpsEvent>>();

        foreach (var evt in events)
        {
            if (evt.EventSource == PrEventSource.Bot && evt.EventMeaning == EventMeaning.Comment)
            {
                if (!botGroups.TryGetValue(evt.PullRequestId, out var group))
                    botGroups[evt.PullRequestId] = group = [];
                group.Add(evt);
            }
            else if (evt.EventSource == PrEventSource.System)
            {
                if (!systemGroups.TryGetValue(evt.PullRequestId, out var group))
                    systemGroups[evt.PullRequestId] = group = [];
                group.Add(evt);
            }
            else
            {
                result.Add(evt);
            }
        }

        foreach (var (_, group) in botGroups)
            result.Add(CollapseGroup(group, PrEventSource.Bot, pollTime));

        foreach (var (_, group) in systemGroups)
            result.Add(CollapseGroup(group, PrEventSource.System, pollTime));

        return result.OrderBy(e => e.CreatedAtUtc).ToList();
    }

    private DevOpsEvent CollapseGroup(List<DevOpsEvent> group, PrEventSource source, DateTimeOffset pollTime)
    {
        if (group.Count == 1) return group[0];

        var representative = group.OrderByDescending(e => e.CreatedAtUtc).First();
        var collapsed = new DevOpsEvent
        {
            EventId = _normalizer.BuildCollapsedEventId(representative.PullRequestId, source, pollTime),
            EventType = representative.EventType,
            EventSource = source,
            EventMeaning = representative.EventMeaning,
            PullRequestId = representative.PullRequestId,
            PullRequestTitle = representative.PullRequestTitle,
            PullRequestUrl = representative.PullRequestUrl,
            Organization = representative.Organization,
            Project = representative.Project,
            Repository = representative.Repository,
            AuthorDisplayName = representative.AuthorDisplayName,
            AuthorCanonicalKey = representative.AuthorCanonicalKey,
            MessageText = representative.MessageText,
            Status = representative.Status,
            CreatedAtUtc = representative.CreatedAtUtc,
            DiscoveredAtUtc = representative.DiscoveredAtUtc,
            LinkedWorkItemId = representative.LinkedWorkItemId,
            IsCurrentUserReviewer = representative.IsCurrentUserReviewer,
            CollapsedCount = group.Count
        };

        return collapsed;
    }
}
