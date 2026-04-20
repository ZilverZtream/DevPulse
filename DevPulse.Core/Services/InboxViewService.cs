using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class InboxViewService
{
    private readonly IStateStore _store;

    public InboxViewService(IStateStore store) => _store = store;

    public Task<IReadOnlyList<DevOpsEvent>> GetLatestAsync(string inboxName, int maxCount, CancellationToken ct = default)
        => _store.GetLatestEventsForInboxAsync(inboxName, maxCount, ct);

    public Task MarkReadAsync(IEnumerable<string> eventIds, CancellationToken ct = default)
        => _store.MarkEventsReadAsync(eventIds, ct);

    public Task<int> GetUnreadCountAsync(string inboxName, CancellationToken ct = default)
        => _store.GetUnreadCountForInboxAsync(inboxName, ct);
}
