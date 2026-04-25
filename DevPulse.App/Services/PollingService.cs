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
    private static bool s_warnedNoSystemInbox;

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
        var pollStart = DateTimeOffset.UtcNow;
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
        // Tuple-dedup by (PrId, RepositoryId) — defensive against API changes or cross-project polling
        // where PR IDs could collide. ADO project-scoped IDs make this equivalent to PrId-only in practice.
        prs = prs.DistinctBy(pr => (pr.PullRequestId, pr.RepositoryId)).ToList();

        // Drop PRs that closed outside the lookback window so first-poll on a long-lived repo doesn't
        // flood the inbox with years of historical merges. Active PRs (no ClosedDate) always stay.
        if (appSettings.PrLookbackDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-appSettings.PrLookbackDays);
            prs = prs.Where(pr => pr.ClosedDate == null || pr.ClosedDate.Value >= cutoff).ToList();
        }
        apiCallCount++;

        var allNewEvents = new List<DevOpsEvent>();
        // Event discovery timestamp — used for DiscoveredAtUtc on events and as a fallback
        // CreatedAtUtc for votes/reviewer-added (ADO doesn't expose vote timestamps).
        var eventDiscoveryTime = DateTimeOffset.UtcNow;

        // Gather per-PR snapshot data and status/vote events
        var prSnapshots = new List<(PullRequestDto Pr, string? PrevStatus, string? PrevVotesJson, Dictionary<string, int> CurrVotes)>();
        var prIdList = prs.Select(pr => pr.PullRequestId).ToList();
        var existingSnapshots = await _store.GetPrSnapshotsAsync(prIdList, ct);

        foreach (var pr in prs)
        {
            ct.ThrowIfCancellationRequested();
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
                    allNewEvents.Add(BuildStatusEvent(pr, meaning, appSettings, idNorm, eventDiscoveryTime));
            }

            if (prevVotesJson != null)
            {
                Dictionary<string, int>? prevVotes;
                try { prevVotes = JsonSerializer.Deserialize<Dictionary<string, int>>(prevVotesJson); }
                catch (JsonException ex)
                {
                    Log.Warning(ex, "PR {PrId} had corrupt vote snapshot; skipping vote/reviewer events until next snapshot", pr.PullRequestId);
                    prevVotes = null;
                }
                if (prevVotes != null)
                {
                    foreach (var reviewer in pr.Reviewers)
                    {
                        if (string.IsNullOrEmpty(reviewer.Id)) continue;
                        if (prevVotes.TryGetValue(reviewer.Id, out var prevVote) && prevVote != reviewer.Vote)
                            allNewEvents.Add(BuildVoteEvent(pr, reviewer, appSettings, idNorm, eventDiscoveryTime));
                        else if (!prevVotes.ContainsKey(reviewer.Id))
                            allNewEvents.Add(BuildReviewerAddedEvent(pr, reviewer, appSettings, idNorm, eventDiscoveryTime));
                    }
                }
            }

            prSnapshots.Add((pr, prevStatus, prevVotesJson, currVotes));
        }

        // Parallel bounded thread fetch — parallelism configurable via AppSettings.
        var parallelism = Math.Clamp(appSettings.PrThreadFetchParallelism, 1, 16);
        using var fetchSemaphore = new SemaphoreSlim(parallelism, parallelism);
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

        // Build comment events (skip already-known); snapshots batched after the loop.
        // Key by (PrId, RepositoryId) to match the tuple-dedup on line 54 — two repos in the same
        // project could legitimately share a PR number under some ADO deployments.
        var snapshotsByPrKey = prSnapshots.ToDictionary(s => (s.Pr.PullRequestId, s.Pr.RepositoryId));
        var snapshotBatch = new List<(int, string, string)>();
        foreach (var (pr, threads) in prWithThreads)
        {
            var snapshotEntry = snapshotsByPrKey[(pr.PullRequestId, pr.RepositoryId)];
            snapshotBatch.Add((pr.PullRequestId, pr.Status, JsonSerializer.Serialize(snapshotEntry.CurrVotes)));

            // First-seen PRs seed their snapshot silently and don't emit historical comment events.
            // Mirrors the prevStatus/prevVotesJson null-guards used for status and vote events above.
            var isFirstSeen = snapshotEntry.PrevStatus == null && snapshotEntry.PrevVotesJson == null;
            if (isFirstSeen) continue;

            var currentUserIsReviewer = IsCurrentUserReviewer(pr, idNorm, appSettings.CurrentUserCanonicalKey);

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
                        DiscoveredAtUtc = eventDiscoveryTime,
                        SourceThreadId = thread.Id.ToString(),
                        SourceCommentId = comment.Id.ToString(),
                        IsCurrentUserReviewer = currentUserIsReviewer
                    });
                }
            }
        }

        // Filter already-known status/vote events using the batch result
        var newEvents = allNewEvents.Where(evt => !existingIds.Contains(evt.EventId)).ToList();

        // Mute before collapse (deliberate deviation from spec): filter out muted events first so the
        // collapser doesn't waste work forming summaries that would be suppressed anyway. End behaviour
        // is equivalent for typical bot-author mutes (all bot comments share one author, so the collapsed
        // row would be muted too). Change the order only if you need the UI to reflect "muted group of N".
        var unmuted = _muteService.Filter(newEvents, activeMutes, eventDiscoveryTime);
        var collapsed = _collapser.Collapse(unmuted);

        if (!inboxes.Any(i => i.IsSystemInbox) && !s_warnedNoSystemInbox)
        {
            Log.Warning("Poll '{Track}': no system inbox configured — NeedsMyAttention events will not be routed", TrackName);
            s_warnedNoSystemInbox = true;
        }

        // Re-fetch inboxes immediately before assignment so a concurrent rename/delete during the
        // poll cycle (e.g., user saved Settings mid-poll) doesn't orphan new events under a
        // stale inbox name. A tiny race window remains between re-fetch and save; acceptable at MINOR.
        var freshInboxes = await _settings.GetInboxDefinitionsAsync(ct);
        var freshWatchers = await _settings.GetWatchersAsync(ct);
        var freshPacks = await _settings.GetKeywordPacksAsync(ct);
        var freshSettings = await _settings.GetAppSettingsAsync(ct);
        var freshAliases = await _settings.GetIdentityAliasesAsync(ct);
        var freshIdNorm = new IdentityNormalizer(freshAliases, freshSettings.BotIdentityPatterns);

        // Recompute IsCurrentUserReviewer using fresh settings — the flag baked into each event
        // during build used the poll-start CurrentUserCanonicalKey. A mid-cycle Settings edit would
        // otherwise feed a stale flag into RuleEngine.MatchesNeedsMyAttention.
        var prsByEventKey = prs
            .GroupBy(p => (p.PullRequestId, p.RepositoryName))
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var evt in collapsed)
        {
            if (prsByEventKey.TryGetValue((evt.PullRequestId, evt.Repository), out var pr))
                evt.IsCurrentUserReviewer = IsCurrentUserReviewer(pr, freshIdNorm, freshSettings.CurrentUserCanonicalKey);
        }

        foreach (var evt in collapsed)
        {
            var (inboxName, ruleDescription) = _ruleEngine.AssignInbox(evt, freshWatchers, freshInboxes, freshPacks, freshSettings);
            evt.InboxName = inboxName;
            evt.MatchedRuleDescription = ruleDescription;
            _debugLog.RecordEvent(evt);
        }

        var preSaveExisting = await _store.GetExistingEventIdsAsync(collapsed.Select(e => e.EventId), ct);
        await _store.SaveEventsAsync(collapsed, ct);

        // Save snapshots AFTER events: if we crash between these calls, the next poll will read the old
        // snapshots, recompute the same diffs, and rebuild the events — dedup catches the duplicates.
        // The reverse order would lose events on crash (snapshots updated → no diff → nothing to replay).
        await _store.SavePrSnapshotsAsync(snapshotBatch, ct);

        // Only query is_read carry-over if any multi-item collapse happened; otherwise no row could have inherited read state.
        var hasCollapsed = collapsed.Any(e => e.CollapsedCount > 1);
        HashSet<string> inheritedReadIds = [];
        if (hasCollapsed)
        {
            var newEventIds = collapsed.Where(e => !preSaveExisting.Contains(e.EventId)).Select(e => e.EventId).ToList();
            if (newEventIds.Count > 0)
                inheritedReadIds = await _store.GetReadEventIdsAsync(newEventIds, ct);
        }

        var totalCandidateCount = candidateCommentIds.Count + candidateStatusVoteIds.Count;
        Log.Information("Poll 'prs': {PrCount} PRs, {Candidates} candidate events, {New} new, {Muted} muted, {Saved} saved",
            prs.Count, totalCandidateCount, newEvents.Count, newEvents.Count - unmuted.Count, collapsed.Count - preSaveExisting.Count);

        var notifiedIds = new List<string>();
        foreach (var evt in collapsed)
        {
            if (preSaveExisting.Contains(evt.EventId)) continue;
            if (inheritedReadIds.Contains(evt.EventId)) continue;

            // Use freshInboxes (same snapshot used to assign evt.InboxName). Looking up in the
            // original poll-start snapshot would miss the inbox if a rename happened mid-cycle.
            var inbox = freshInboxes.FirstOrDefault(i => i.Name.Equals(evt.InboxName, StringComparison.OrdinalIgnoreCase));
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

        await _store.CleanStaleSnapshotsAsync(prIdList, 30, ct);

        await _store.SetLastSuccessfulPollAsync("prs", pollStart, ct);
        _debugLog.UpdatePollStatus("prs", pollStart, pollStart.AddMinutes(appSettings.PrPollingIntervalMinutes), apiCallCount);
    }

    protected override async Task OnPollFailedAsync(Exception ex, CancellationToken ct)
    {
        var kind = PollErrorClassifier.Classify(ex);
        _debugLog.UpdatePollStatus("prs", await _store.GetLastSuccessfulPollAsync("prs", ct), null, 0, $"{kind}: {ex.Message}");
    }

    private DevOpsEvent BuildStatusEvent(PullRequestDto pr, EventMeaning meaning, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset eventDiscoveryTime)
    {
        var actor = pr.CompletedBy ?? pr.CreatedBy ?? new IdentityRefDto();
        return new DevOpsEvent
        {
            EventId = _eventNorm.BuildStatusEventId(pr.PullRequestId, pr.Status, pr.ClosedDate ?? eventDiscoveryTime),
            EventType = meaning == EventMeaning.Merged ? DevOpsEventType.PullRequestCompleted : DevOpsEventType.PullRequestAbandoned,
            EventSource = idNorm.ClassifySource(actor),
            EventMeaning = meaning,
            PullRequestId = pr.PullRequestId,
            PullRequestTitle = pr.Title,
            PullRequestUrl = pr.Url,
            Organization = pr.Organization,
            Project = pr.Project,
            Repository = pr.RepositoryName,
            AuthorDisplayName = actor.DisplayName ?? string.Empty,
            AuthorCanonicalKey = idNorm.Normalize(actor),
            Status = pr.Status,
            CreatedAtUtc = pr.ClosedDate ?? eventDiscoveryTime,
            DiscoveredAtUtc = eventDiscoveryTime,
            IsCurrentUserReviewer = IsCurrentUserReviewer(pr, idNorm, settings.CurrentUserCanonicalKey)
        };
    }

    private DevOpsEvent BuildVoteEvent(PullRequestDto pr, ReviewerDto reviewer, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset eventDiscoveryTime)
    {
        var identity = reviewer.AsIdentityRef();
        var meaning = _eventNorm.DeriveVoteMeaning(reviewer.Vote);
        return new DevOpsEvent
        {
            EventId = _eventNorm.BuildVoteEventId(pr.PullRequestId, reviewer.Id, reviewer.Vote, eventDiscoveryTime),
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
            CreatedAtUtc = eventDiscoveryTime,
            DiscoveredAtUtc = eventDiscoveryTime,
            IsCurrentUserReviewer = IsCurrentUserReviewer(pr, idNorm, settings.CurrentUserCanonicalKey)
        };
    }

    private DevOpsEvent BuildReviewerAddedEvent(PullRequestDto pr, ReviewerDto reviewer, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset eventDiscoveryTime)
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
            CreatedAtUtc = eventDiscoveryTime,
            DiscoveredAtUtc = eventDiscoveryTime,
            IsCurrentUserReviewer = IsCurrentUserReviewer(pr, idNorm, settings.CurrentUserCanonicalKey)
        };
    }

    private static bool IsCurrentUserReviewer(PullRequestDto pr, IdentityNormalizer idNorm, string canonicalKey)
    {
        if (string.IsNullOrEmpty(canonicalKey)) return false;
        return pr.Reviewers.Any(r =>
        {
            var normalized = idNorm.Normalize(r.AsIdentityRef());
            return !string.IsNullOrEmpty(normalized) &&
                   normalized.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase);
        });
    }
}
