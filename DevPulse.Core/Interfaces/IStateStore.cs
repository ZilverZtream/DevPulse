using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IStateStore
{
    Task<bool> EventExistsAsync(string eventId, CancellationToken ct = default);
    Task<HashSet<string>> GetExistingEventIdsAsync(IEnumerable<string> candidateIds, CancellationToken ct = default);
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
    Task<(string? Status, string? VotesJson)> GetPrSnapshotAsync(int prId, CancellationToken ct = default);
    Task CleanStaleSnapshotsAsync(int retainDays = 30, CancellationToken ct = default);

    Task<int> GetUnreadCountForInboxAsync(string inboxName, CancellationToken ct = default);
    Task MarkNotificationSentAsync(IEnumerable<string> eventIds, CancellationToken ct = default);
    Task RenameInboxAsync(string oldName, string newName, CancellationToken ct = default);
}
