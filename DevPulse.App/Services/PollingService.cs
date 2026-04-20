using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Services;

public sealed class PollingService : PollingLoopBase
{
    private readonly IAzureDevOpsClient _adoClient;
    private readonly IStateStore _store;
    private readonly INotificationService _notifications;
    private readonly SettingsService _settings;
    private readonly DebugLogService _debugLog;
    private readonly RuleEngine _ruleEngine = new();
    private readonly EventCollapser _collapser = new();
    private readonly MuteService _muteService = new();
    private readonly EventNormalizer _eventNorm = new();

    public PollingService(
        IAzureDevOpsClient adoClient,
        IStateStore store,
        INotificationService notifications,
        SettingsService settings,
        DebugLogService debugLog)
    {
        _adoClient = adoClient;
        _store = store;
        _notifications = notifications;
        _settings = settings;
        _debugLog = debugLog;
    }

    protected override string TrackName => "prs";

    protected override async Task ExecutePollAsync(CancellationToken ct)
    {
        var apiCallCount = 0;
        var appSettings = await _settings.GetAppSettingsAsync();
        var inboxes = await _settings.GetInboxDefinitionsAsync();
        var packs = await _settings.GetKeywordPacksAsync();
        var aliases = await _settings.GetIdentityAliasesAsync();
        var watchers = await _settings.GetWatchersAsync();
        await _store.PurgeExpiredMutesAsync(ct);
        var activeMutes = await _store.GetActiveMutesAsync(ct);

        var idNorm = new IdentityNormalizer(aliases, appSettings.BotIdentityPatterns);

        var prs = await _adoClient.GetRelevantPullRequestsAsync(ct);
        prs = prs.DistinctBy(pr => pr.PullRequestId).ToList();
        apiCallCount++;

        var allNewEvents = new List<DevOpsEvent>();
        var pollTime = DateTimeOffset.UtcNow;

        // Gather per-PR snapshot data and status/vote events
        var prSnapshots = new List<(PullRequestDto Pr, string? PrevStatus, string? PrevVotesJson, Dictionary<string, int> CurrVotes)>();
        var prIdList = prs.Select(pr => pr.PullRequestId).ToList();
        var existingSnapshots = await _store.GetPrSnapshotsAsync(prIdList, ct);

        foreach (var pr in prs)
        {
            existingSnapshots.TryGetValue(pr.PullRequestId, out var snap);
            var prevStatus = snap.Status;
            var prevVotesJson = snap.VotesJson;
            var currVotes = pr.Reviewers
                .Where(r => !string.IsNullOrEmpty(r.Id))
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.Last().Vote);

            if (prevStatus != null && !prevStatus.Equals(pr.Status, StringComparison.OrdinalIgnoreCase))
            {
                var meaning = _eventNorm.DeriveStatusMeaning(pr.Status);
                if (meaning != EventMeaning.Unknown)
                    allNewEvents.Add(BuildStatusEvent(pr, meaning, appSettings, idNorm, pollTime));
            }

            if (prevVotesJson != null)
            {
                Dictionary<string, int> prevVotes;
                try { prevVotes = JsonSerializer.Deserialize<Dictionary<string, int>>(prevVotesJson) ?? []; }
                catch (JsonException ex)
                {
                    Log.Warning(ex, "PR {PrId} had corrupt vote snapshot; treating as empty", pr.PullRequestId);
                    prevVotes = [];
                }
                foreach (var reviewer in pr.Reviewers)
                {
                    if (string.IsNullOrEmpty(reviewer.Id)) continue;
                    if (prevVotes.TryGetValue(reviewer.Id, out var prevVote) && prevVote != reviewer.Vote)
                        allNewEvents.Add(BuildVoteEvent(pr, reviewer, appSettings, idNorm, pollTime));
                    else if (!prevVotes.ContainsKey(reviewer.Id))
                        allNewEvents.Add(BuildReviewerAddedEvent(pr, reviewer, appSettings, idNorm, pollTime));
                }
            }

            prSnapshots.Add((pr, prevStatus, prevVotesJson, currVotes));
        }

        // Parallel bounded thread fetch
        using var fetchSemaphore = new SemaphoreSlim(4, 4);
        var threadTasks = prs.Select(async pr =>
        {
            await fetchSemaphore.WaitAsync(ct);
            try
            {
                var threads = await _adoClient.GetPullRequestThreadsAsync(pr.PullRequestId, pr.RepositoryId, ct);
                Interlocked.Increment(ref apiCallCount);
                return (pr, threads);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Thread fetch failed for PR #{PrId}; treating as empty", pr.PullRequestId);
                return (pr, (IReadOnlyList<PullRequestThreadDto>)[]);
            }
            finally { fetchSemaphore.Release(); }
        }).ToArray();
        var prWithThreads = await Task.WhenAll(threadTasks);

        // Collect candidate comment event IDs for batch dedup
        var candidateCommentIds = new List<string>();
        foreach (var (pr, threads) in prWithThreads)
        {
            foreach (var thread in threads)
                foreach (var comment in thread.Comments.Where(c => c.ParentCommentId == 0))
                    candidateCommentIds.Add(_eventNorm.BuildCommentEventId(pr.PullRequestId, thread.Id, comment.Id));
        }

        // Batch status/vote/reviewer event IDs for dedup
        var candidateStatusVoteIds = allNewEvents.Select(e => e.EventId).ToList();
        var allCandidateIds = candidateCommentIds.Concat(candidateStatusVoteIds).ToList();
        var existingIds = await _store.GetExistingEventIdsAsync(allCandidateIds, ct);

        // Build comment events (skip already-known), save snapshots
        var snapshotsByPrId = prSnapshots.ToDictionary(s => s.Pr.PullRequestId);
        foreach (var (pr, threads) in prWithThreads)
        {
            var snapshotEntry = snapshotsByPrId[pr.PullRequestId];
            await _store.SavePrSnapshotAsync(pr.PullRequestId, pr.Status, JsonSerializer.Serialize(snapshotEntry.CurrVotes), ct);

            var currentUserIsReviewer = pr.Reviewers.Any(r =>
                !string.IsNullOrEmpty(r.UniqueName) && r.UniqueName.Equals(appSettings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase));

            foreach (var thread in threads)
            {
                foreach (var comment in thread.Comments.Where(c => c.ParentCommentId == 0))
                {
                    var eventId = _eventNorm.BuildCommentEventId(pr.PullRequestId, thread.Id, comment.Id);
                    if (existingIds.Contains(eventId)) continue;

                    var authorIdentity = comment.Author ?? new IdentityRefDto();
                    var authorCanon = idNorm.Normalize(authorIdentity);
                    var source = idNorm.ClassifySource(authorIdentity);
                    var meaning = _eventNorm.DeriveCommentMeaning(comment.Content, appSettings.CurrentUserCanonicalKey, appSettings.CurrentUserDisplayName);

                    allNewEvents.Add(new DevOpsEvent
                    {
                        EventId = eventId,
                        EventType = DevOpsEventType.CommentAdded,
                        EventSource = source,
                        EventMeaning = meaning,
                        PullRequestId = pr.PullRequestId,
                        PullRequestTitle = pr.Title,
                        PullRequestUrl = pr.Url,
                        Organization = pr.Organization,
                        Project = pr.Project,
                        Repository = pr.RepositoryName,
                        AuthorDisplayName = authorIdentity.DisplayName ?? string.Empty,
                        AuthorCanonicalKey = authorCanon,
                        MessageText = comment.Content,
                        Status = pr.Status,
                        CreatedAtUtc = comment.PublishedDate,
                        DiscoveredAtUtc = pollTime,
                        SourceThreadId = thread.Id.ToString(),
                        SourceCommentId = comment.Id.ToString(),
                        IsCurrentUserReviewer = currentUserIsReviewer
                    });
                }
            }
        }

        // Filter already-known status/vote events using the batch result
        var newEvents = allNewEvents.Where(evt => !existingIds.Contains(evt.EventId)).ToList();

        var unmuted = _muteService.Filter(newEvents, activeMutes, pollTime);
        var collapsed = _collapser.Collapse(unmuted, pollTime);

        if (!inboxes.Any(i => i.IsSystemInbox))
            Log.Warning("Poll '{Track}': no system inbox configured — NeedsMyAttention events will not be routed", TrackName);

        foreach (var evt in collapsed)
        {
            var (inboxName, ruleDescription) = _ruleEngine.AssignInbox(evt, watchers, inboxes, packs, appSettings);
            evt.InboxName = inboxName;
            evt.MatchedRuleDescription = ruleDescription;
            _debugLog.RecordEvent(evt);
        }

        await _store.SaveEventsAsync(collapsed, ct);

        Log.Information("Poll 'prs': {PrCount} PRs, {Candidates} candidate events, {New} new, {Muted} muted, {Saved} saved",
            prs.Count, allNewEvents.Count, newEvents.Count, newEvents.Count - unmuted.Count, collapsed.Count);

        var notifiedIds = new List<string>();
        foreach (var evt in collapsed)
        {
            var inbox = inboxes.FirstOrDefault(i => i.Name.Equals(evt.InboxName, StringComparison.OrdinalIgnoreCase));
            if (inbox?.ShowNotifications == true)
            {
                try
                {
                    await _notifications.ShowAsync(evt);
                    notifiedIds.Add(evt.EventId);
                }
                catch (Exception ex) { Log.Warning(ex, "Notification failed for event {EventId}", evt.EventId); }
            }
        }
        if (notifiedIds.Count > 0)
            await _store.MarkNotificationSentAsync(notifiedIds, ct);

        await _store.CleanStaleSnapshotsAsync(30, ct);

        var now = DateTimeOffset.UtcNow;
        await _store.SetLastSuccessfulPollAsync("prs", now, ct);
        _debugLog.UpdatePollStatus("prs", now, now.AddMinutes(appSettings.PrPollingIntervalMinutes), apiCallCount);
    }

    protected override async Task OnPollFailedAsync(Exception ex, CancellationToken ct)
    {
        _debugLog.UpdatePollStatus("prs", await _store.GetLastSuccessfulPollAsync("prs", ct), null, 0, PollErrorClassifier.Classify(ex));
    }

    private DevOpsEvent BuildStatusEvent(PullRequestDto pr, EventMeaning meaning, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset pollTime)
    {
        var actor = pr.CompletedBy ?? pr.CreatedBy;
        return new DevOpsEvent
        {
            EventId = _eventNorm.BuildStatusEventId(pr.PullRequestId, pr.Status, pr.ClosedDate ?? pollTime),
            EventType = meaning == EventMeaning.Merged ? DevOpsEventType.PullRequestCompleted : DevOpsEventType.PullRequestAbandoned,
            EventSource = idNorm.ClassifySource(actor),
            EventMeaning = meaning,
            PullRequestId = pr.PullRequestId,
            PullRequestTitle = pr.Title,
            PullRequestUrl = pr.Url,
            Organization = pr.Organization,
            Project = pr.Project,
            Repository = pr.RepositoryName,
            AuthorDisplayName = actor.DisplayName,
            AuthorCanonicalKey = idNorm.Normalize(actor),
            Status = pr.Status,
            CreatedAtUtc = pr.ClosedDate ?? pollTime,
            DiscoveredAtUtc = pollTime,
            IsCurrentUserReviewer = pr.Reviewers.Any(r => !string.IsNullOrEmpty(r.UniqueName) && r.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase))
        };
    }

    private DevOpsEvent BuildVoteEvent(PullRequestDto pr, ReviewerDto reviewer, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset pollTime)
    {
        var identity = reviewer.AsIdentityRef();
        var meaning = _eventNorm.DeriveVoteMeaning(reviewer.Vote);
        return new DevOpsEvent
        {
            EventId = _eventNorm.BuildVoteEventId(pr.PullRequestId, reviewer.Id, reviewer.Vote, pollTime),
            EventType = DevOpsEventType.ReviewerVoteChanged,
            EventSource = idNorm.ClassifySource(identity),
            EventMeaning = meaning,
            PullRequestId = pr.PullRequestId,
            PullRequestTitle = pr.Title,
            PullRequestUrl = pr.Url,
            Organization = pr.Organization,
            Project = pr.Project,
            Repository = pr.RepositoryName,
            AuthorDisplayName = reviewer.DisplayName,
            AuthorCanonicalKey = idNorm.Normalize(identity),
            MessageText = $"Vote: {reviewer.Vote}",
            Status = pr.Status,
            CreatedAtUtc = pollTime,
            DiscoveredAtUtc = pollTime,
            IsCurrentUserReviewer = pr.Reviewers.Any(r => !string.IsNullOrEmpty(r.UniqueName) && r.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase))
        };
    }

    private DevOpsEvent BuildReviewerAddedEvent(PullRequestDto pr, ReviewerDto reviewer, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset pollTime)
    {
        var identity = reviewer.AsIdentityRef();
        return new DevOpsEvent
        {
            EventId = _eventNorm.BuildReviewerAddedEventId(pr.PullRequestId, reviewer.Id),
            EventType = DevOpsEventType.ReviewerAdded,
            EventSource = idNorm.ClassifySource(identity),
            EventMeaning = EventMeaning.ReviewerAdded,
            PullRequestId = pr.PullRequestId,
            PullRequestTitle = pr.Title,
            PullRequestUrl = pr.Url,
            Organization = pr.Organization,
            Project = pr.Project,
            Repository = pr.RepositoryName,
            AuthorDisplayName = reviewer.DisplayName,
            AuthorCanonicalKey = idNorm.Normalize(identity),
            Status = pr.Status,
            CreatedAtUtc = pollTime,
            DiscoveredAtUtc = pollTime,
            IsCurrentUserReviewer = !string.IsNullOrEmpty(reviewer.UniqueName) && reviewer.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase)
        };
    }
}
