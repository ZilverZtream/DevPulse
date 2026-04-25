using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class IdentityNormalizer
{
    private readonly List<IdentityAlias> _aliases;
    private readonly List<string> _botPatterns;

    public IdentityNormalizer(IEnumerable<IdentityAlias> aliases, IEnumerable<string> botPatterns)
    {
        _aliases = [.. aliases];
        _botPatterns = [.. botPatterns];
    }

    public string Normalize(IdentityRefDto identity)
    {
        var raw = string.IsNullOrWhiteSpace(identity.UniqueName)
            ? identity.DisplayName ?? string.Empty
            : identity.UniqueName;

        foreach (var alias in _aliases)
        {
            // Null-safe: JSON-deserialized Variants lists can contain null entries (e.g., `["alice", null]`).
            if (alias.Variants.Any(v => (v?.Equals(raw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                        (v?.Equals(identity.DisplayName, StringComparison.OrdinalIgnoreCase) ?? false)))
                return alias.CanonicalKey;
        }

        return raw;
    }

    public PrEventSource ClassifySource(IdentityRefDto identity)
    {
        var canonical = Normalize(identity);
        var display = identity.DisplayName ?? string.Empty;

        foreach (var pattern in _botPatterns)
        {
            if (canonical.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
                display.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return PrEventSource.Bot;
        }

        return PrEventSource.Human;
    }
}
