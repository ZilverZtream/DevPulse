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
            if (mute.ExpiresAtUtc.HasValue && mute.ExpiresAtUtc.Value <= now) continue;

            if (mute.Scope == MuteScope.PullRequest && mute.PrId == evt.PullRequestId)
                return true;

            // Reject empty-key author mutes up front — an empty mute AuthorKey would otherwise match
            // every comment from a deleted/system user (which normalises to empty canonical key).
            if (mute.Scope == MuteScope.Author &&
                !string.IsNullOrEmpty(mute.AuthorKey) &&
                !string.IsNullOrEmpty(evt.AuthorCanonicalKey) &&
                mute.AuthorKey.Equals(evt.AuthorCanonicalKey, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Cap on bounded mutes — a snooze saved with a far-future expiry would silently persist for years.
    // Permanent mutes (CreatePrMute / CreateAuthorMutePermanent) explicitly opt out by passing null expiry.
    public static readonly TimeSpan MaxMuteDuration = TimeSpan.FromDays(90);

    public static MuteEntry CreatePrMute(int prId) => new()
    {
        Scope = MuteScope.PullRequest,
        PrId = prId
    };

    public static MuteEntry CreatePrSnooze(int prId, DateTimeOffset expiresAt)
    {
        var maxAllowed = DateTimeOffset.UtcNow.Add(MaxMuteDuration);
        if (expiresAt > maxAllowed)
            throw new ArgumentException($"Snooze duration cannot exceed {MaxMuteDuration.TotalDays} days.", nameof(expiresAt));
        return new MuteEntry
        {
            Scope = MuteScope.PullRequest,
            PrId = prId,
            ExpiresAtUtc = expiresAt
        };
    }

    public static MuteEntry CreateAuthorMuteToday(string canonicalKey, DateTimeOffset now) => new()
    {
        Scope = MuteScope.Author,
        AuthorKey = canonicalKey,
        ExpiresAtUtc = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero)
    };

    public static MuteEntry CreateAuthorMutePermanent(string canonicalKey) => new()
    {
        Scope = MuteScope.Author,
        AuthorKey = canonicalKey
    };
}
