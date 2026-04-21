using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IStateStore
{
    Task<bool> EventExistsAsync(string eventId, CancellationToken ct = default);
    Task<HashSet<string>> GetExistingEventIdsAsync(IEnumerable<string> candidateIds, CancellationToken ct = default);
    Task<HashSet<string>> GetReadEventIdsAsync(IEnumerable<string> eventIds, CancellationToken ct = default);
    Task SaveEventsAsync(IEnumerable<DevOpsEvent> events, CancellationToken ct = default);
    Task<IReadOnlyList<DevOpsEvent>> GetLatestEventsForInboxAsync(string inboxName, int maxCount, CancellationToken ct = default);
    Task MarkEventsReadAsync(IEnumerable<string> eventIds, CancellationToken ct = default);

    Task UpsertWorkItemsAsync(IEnumerable<WorkItem> items, CancellationToken ct = default);
    Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(CancellationToken ct = default);

    Task SaveMuteEntryAsync(MuteEntry entry, CancellationToken ct = default);
    Task RemoveMuteEntryAsync(MuteEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<MuteEntry>> GetActiveMutesAsync(CancellationToken ct = default);
    Task PurgeExpiredMutesAsync(CancellationToken ct = default);

    Task<DateTimeOffset?> GetLastSuccessfulPollAsync(string track, CancellationToken ct = default);
    Task SetLastSuccessfulPollAsync(string track, DateTimeOffset ts, CancellationToken ct = default);

    Task SavePrSnapshotAsync(int prId, string status, string votesJson, CancellationToken ct = default);
    Task SavePrSnapshotsAsync(IReadOnlyList<(int PrId, string Status, string VotesJson)> snapshots, CancellationToken ct = default);
    Task<Dictionary<int, (string? Status, string? VotesJson)>> GetPrSnapshotsAsync(IEnumerable<int> prIds, CancellationToken ct = default);
    Task CleanStaleSnapshotsAsync(IReadOnlyList<int> activePrIds, int retainDays = 30, CancellationToken ct = default);

    Task<int> GetUnreadCountForInboxAsync(string inboxName, CancellationToken ct = default);
    Task MarkNotificationSentAsync(IEnumerable<string> eventIds, CancellationToken ct = default);
    Task MarkInboxReadAsync(string inboxName, CancellationToken ct = default);

    Task ApplyInboxChangesAsync(
        IReadOnlyList<(string OldName, string NewName)> renames,
        IReadOnlyList<string> deletions,
        IReadOnlyList<InboxDefinition> newInboxes,
        CancellationToken ct = default);

    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);

    // AI attempts
    Task RecordAiAttemptAsync(AiAttempt attempt, CancellationToken ct = default);
    Task<IReadOnlyList<AiAttempt>> GetAiAttemptsForWorkItemAsync(int workItemId, CancellationToken ct = default);
}
