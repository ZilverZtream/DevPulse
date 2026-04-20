using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Services;

public sealed class PollingService : IDisposable
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

    private System.Threading.Timer? _timer;
    private int _running; // 0=idle, 1=running (interlocked)
    private int _apiCallCount;

    public event EventHandler? PollCompleted;

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

    public void Start(int intervalMinutes)
    {
        var ms = intervalMinutes * 60 * 1000;
        _timer = new System.Threading.Timer(async _ => await RunCycleAsync(), null, 0, ms);
    }

    public async Task RefreshNowAsync() => await RunCycleAsync();

    private async Task RunCycleAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        try
        {
            await ExecutePollAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PR poll cycle failed");
            _debugLog.UpdatePollStatus("prs", await _store.GetLastSuccessfulPollAsync("prs"), null, _apiCallCount, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task ExecutePollAsync()
    {
        _apiCallCount = 0;
        var appSettings = await _settings.GetAppSettingsAsync();
        var inboxes = await _settings.GetInboxDefinitionsAsync();
        var packs = await _settings.GetKeywordPacksAsync();
        var aliases = await _settings.GetIdentityAliasesAsync();
        var watchers = await _settings.GetWatchersAsync();
        var activeMutes = await _store.GetActiveMutesAsync();

        var idNorm = new IdentityNormalizer(aliases, appSettings.BotIdentityPatterns);

        var prs = await _adoClient.GetRelevantPullRequestsAsync();
        _apiCallCount++;

        var allNewEvents = new List<DevOpsEvent>();
        var pollTime = DateTimeOffset.UtcNow;

        foreach (var pr in prs)
        {
            // Snapshot change detection
            var (prevStatus, prevVotesJson) = await _store.GetPrSnapshotAsync(pr.PullRequestId);
            var currentVotesJson = JsonSerializer.Serialize(pr.Reviewers.ToDictionary(r => r.Id, r => r.Vote));

            // Status-change events
            if (prevStatus != null && !prevStatus.Equals(pr.Status, StringComparison.OrdinalIgnoreCase))
            {
                var meaning = _eventNorm.DeriveStatusMeaning(pr.Status);
                if (meaning != EventMeaning.Unknown)
                {
                    var evt = BuildStatusEvent(pr, meaning, appSettings, idNorm, pollTime);
                    allNewEvents.Add(evt);
                }
            }

            // Reviewer vote-change events
            if (prevVotesJson != null && prevVotesJson != currentVotesJson)
            {
                var prevVotes = JsonSerializer.Deserialize<Dictionary<string, int>>(prevVotesJson) ?? [];
                foreach (var reviewer in pr.Reviewers)
                {
                    if (prevVotes.TryGetValue(reviewer.Id, out var prevVote) && prevVote != reviewer.Vote)
                    {
                        var evt = BuildVoteEvent(pr, reviewer, appSettings, idNorm, pollTime);
                        allNewEvents.Add(evt);
                    }
                    else if (!prevVotes.ContainsKey(reviewer.Id))
                    {
                        var evt = BuildReviewerAddedEvent(pr, reviewer, appSettings, idNorm, pollTime);
                        allNewEvents.Add(evt);
                    }
                }
            }

            // Save snapshot
            await _store.SavePrSnapshotAsync(pr.PullRequestId, pr.Status, currentVotesJson);

            // Threads
            var threads = await _adoClient.GetPullRequestThreadsAsync(pr.PullRequestId, pr.RepositoryId);
            _apiCallCount++;

            var currentUserIsReviewer = pr.Reviewers.Any(r =>
                r.UniqueName.Equals(appSettings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase));

            foreach (var thread in threads)
            {
                foreach (var comment in thread.Comments.Where(c => c.ParentCommentId == 0 || true))
                {
                    var eventId = _eventNorm.BuildCommentEventId(pr.PullRequestId, thread.Id, comment.Id);
                    if (await _store.EventExistsAsync(eventId)) continue;

                    var authorCanon = idNorm.Normalize(comment.Author);
                    var source = idNorm.ClassifySource(comment.Author);
                    var meaning = _eventNorm.DeriveCommentMeaning(comment.Content, appSettings.CurrentUserCanonicalKey, authorCanon);

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
                        AuthorDisplayName = comment.Author.DisplayName,
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

        // Filter already-known (second-pass dedup for status/vote events)
        var newEvents = new List<DevOpsEvent>();
        foreach (var evt in allNewEvents)
        {
            if (!await _store.EventExistsAsync(evt.EventId))
                newEvents.Add(evt);
        }

        // Apply mutes
        var unmuted = _muteService.Filter(newEvents, activeMutes, pollTime);

        // Collapse
        var collapsed = _collapser.Collapse(unmuted, pollTime);

        // Assign inboxes
        foreach (var evt in collapsed)
        {
            evt.InboxName = _ruleEngine.AssignInbox(evt, watchers, inboxes, packs, appSettings);
            _debugLog.RecordEvent(evt);
        }

        // Persist
        await _store.SaveEventsAsync(collapsed);

        // Notifications
        foreach (var evt in collapsed)
        {
            var inbox = inboxes.FirstOrDefault(i => i.Name == evt.InboxName);
            if (inbox?.ShowNotifications == true)
                await _notifications.ShowAsync(evt);
        }

        var now = DateTimeOffset.UtcNow;
        await _store.SetLastSuccessfulPollAsync("prs", now);
        _debugLog.UpdatePollStatus("prs", now, now.AddMinutes(appSettings.PrPollingIntervalMinutes), _apiCallCount);

        PollCompleted?.Invoke(this, EventArgs.Empty);
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
            IsCurrentUserReviewer = pr.Reviewers.Any(r => r.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase))
        };
    }

    private DevOpsEvent BuildVoteEvent(PullRequestDto pr, ReviewerDto reviewer, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset pollTime)
    {
        var identity = new IdentityRefDto { DisplayName = reviewer.DisplayName, UniqueName = reviewer.UniqueName, Id = reviewer.Id };
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
            IsCurrentUserReviewer = pr.Reviewers.Any(r => r.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase))
        };
    }

    private DevOpsEvent BuildReviewerAddedEvent(PullRequestDto pr, ReviewerDto reviewer, AppSettings settings, IdentityNormalizer idNorm, DateTimeOffset pollTime)
    {
        var identity = new IdentityRefDto { DisplayName = reviewer.DisplayName, UniqueName = reviewer.UniqueName, Id = reviewer.Id };
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
            IsCurrentUserReviewer = reviewer.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase)
        };
    }

    public void Dispose() => _timer?.Dispose();
}
