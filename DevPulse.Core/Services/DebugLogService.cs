using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class DebugLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string EventId { get; init; } = string.Empty;
    public string AuthorCanonicalKey { get; init; } = string.Empty;
    public string EventSource { get; init; } = string.Empty;
    public string EventMeaning { get; init; } = string.Empty;
    public string InboxAssigned { get; init; } = string.Empty;
    public string RuleMatched { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public sealed class PollStatusEntry
{
    public string Track { get; init; } = string.Empty;
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public DateTimeOffset? NextScheduledUtc { get; init; }
    public int ApiCallCount { get; init; }
    public string? LastError { get; init; }
}

public sealed class DebugLogService
{
    private readonly int _maxEntries;
    private readonly Queue<DebugLogEntry> _eventLog = new();
    private readonly Dictionary<string, PollStatusEntry> _pollStatus = new();
    private readonly object _lock = new();

    public DebugLogService(int maxEntries = 500) => _maxEntries = maxEntries;

    public void RecordEvent(DevOpsEvent evt)
    {
        var entry = new DebugLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventId = evt.EventId,
            AuthorCanonicalKey = evt.AuthorCanonicalKey,
            EventSource = evt.EventSource.ToString(),
            EventMeaning = evt.EventMeaning.ToString(),
            InboxAssigned = evt.InboxName,
            RuleMatched = evt.MatchedRuleDescription ?? string.Empty
        };
        lock (_lock)
        {
            _eventLog.Enqueue(entry);
            while (_eventLog.Count > _maxEntries)
                _eventLog.Dequeue();
        }
    }

    public void RecordError(string eventId, string error)
    {
        var entry = new DebugLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventId = eventId,
            ErrorMessage = error
        };
        lock (_lock)
        {
            _eventLog.Enqueue(entry);
            while (_eventLog.Count > _maxEntries)
                _eventLog.Dequeue();
        }
    }

    public void UpdatePollStatus(string track, DateTimeOffset? lastSuccess, DateTimeOffset? nextScheduled, int apiCalls, string? error = null)
    {
        lock (_lock)
        {
            _pollStatus[track] = new PollStatusEntry
            {
                Track = track,
                LastSuccessUtc = lastSuccess,
                NextScheduledUtc = nextScheduled,
                ApiCallCount = apiCalls,
                LastError = error
            };
        }
    }

    public IReadOnlyList<DebugLogEntry> GetRecentEvents()
    {
        lock (_lock) return _eventLog.ToList();
    }

    public IReadOnlyList<PollStatusEntry> GetPollStatus()
    {
        lock (_lock) return _pollStatus.Values.ToList();
    }

    public void Clear()
    {
        lock (_lock) _eventLog.Clear();
    }
}
