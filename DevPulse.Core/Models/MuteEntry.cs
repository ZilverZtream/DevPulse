using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class MuteEntry
{
    public MuteScope Scope { get; set; }
    public int? PrId { get; set; }
    public string AuthorKey { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    // DB dedup key — primary key in mute_entries table
    public string DbKey => Scope == MuteScope.PullRequest
        ? (PrId?.ToString() ?? string.Empty)
        : AuthorKey;
}
