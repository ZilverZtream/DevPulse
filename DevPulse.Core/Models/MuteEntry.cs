using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class MuteEntry
{
    public MuteScope Scope { get; set; }
    public int? PrId { get; set; }
    public string AuthorKey { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    /// <summary>
    /// Dual-purpose primary DB key for the mute_entries table: PR ID (as string) when Scope=PullRequest,
    /// author canonical key when Scope=Author. Combined with Scope to form the compound (scope, key) PK.
    /// </summary>
    public string DbKey => Scope == MuteScope.PullRequest
        ? (PrId?.ToString() ?? string.Empty)
        : AuthorKey;
}
