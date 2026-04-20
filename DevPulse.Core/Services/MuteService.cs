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

            if (mute.Scope == MuteScope.PullRequest &&
                mute.Key == evt.PullRequestId.ToString())
                return true;

            if (mute.Scope == MuteScope.Author &&
                mute.Key.Equals(evt.AuthorCanonicalKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static MuteEntry CreatePrMute(int prId) => new()
    {
        Scope = MuteScope.PullRequest,
        Key = prId.ToString(),
        ExpiresAtUtc = null
    };

    public static MuteEntry CreatePrSnooze(int prId, DateTimeOffset expiresAt) => new()
    {
        Scope = MuteScope.PullRequest,
        Key = prId.ToString(),
        ExpiresAtUtc = expiresAt
    };

    public static MuteEntry CreateAuthorMuteToday(string canonicalKey, DateTimeOffset now) => new()
    {
        Scope = MuteScope.Author,
        Key = canonicalKey,
        ExpiresAtUtc = now.Date.AddDays(1)
    };

    public static MuteEntry CreateAuthorMutePermanent(string canonicalKey) => new()
    {
        Scope = MuteScope.Author,
        Key = canonicalKey,
        ExpiresAtUtc = null
    };
}
