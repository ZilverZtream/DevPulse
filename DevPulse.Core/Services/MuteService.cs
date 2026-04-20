using DevPulse.Core.Enums;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class MuteService
{
    public IReadOnlyList<DevOpsEvent> Filter(IReadOnlyList<DevOpsEvent> events, IReadOnlyList<MuteEntry> activeMutes, DateTimeOffset now)
    {
        return events.Where(e => !IsMuted(e, activeMutes, now)).ToList();
    }

    public bool IsMuted(DevOpsEvent evt, IReadOnlyList<MuteEntry> activeMutes, DateTimeOffset now)
    {
        foreach (var mute in activeMutes)
        {
            if (mute.ExpiresAtUtc.HasValue && mute.ExpiresAtUtc.Value <= now)
                continue;

            if (mute.Scope == MuteScope.PullRequest && mute.PrId == evt.PullRequestId)
                return true;

            if (mute.Scope == MuteScope.Author &&
                mute.AuthorKey.Equals(evt.AuthorCanonicalKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static MuteEntry CreatePrMute(int prId) => new()
    {
        Scope = MuteScope.PullRequest,
        PrId = prId
    };

    public static MuteEntry CreatePrSnooze(int prId, DateTimeOffset expiresAt) => new()
    {
        Scope = MuteScope.PullRequest,
        PrId = prId,
        ExpiresAtUtc = expiresAt
    };

    public static MuteEntry CreateAuthorMuteToday(string canonicalKey, DateTimeOffset now) => new()
    {
        Scope = MuteScope.Author,
        AuthorKey = canonicalKey,
        ExpiresAtUtc = now.Date.AddDays(1)
    };

    public static MuteEntry CreateAuthorMutePermanent(string canonicalKey) => new()
    {
        Scope = MuteScope.Author,
        AuthorKey = canonicalKey
    };
}
