using System.IO;
using System.Net.Sockets;

namespace DevPulse.Core.Services;

public enum PollErrorKind
{
    Transient,        // network blip, 5xx, timeout — retry next cycle
    Throttled,        // 429 — already retried via AdoRetryHelper, just log
    AuthRequired,     // 401, 403 — user must re-enter PAT or fix permissions
    Permanent,        // 404 (project deleted), bad config — stop polling, surface to user
    Unknown
}

public static class PollErrorClassifier
{
    public static PollErrorKind Classify(Exception ex)
    {
        // OperationCanceledException isn't really an error — callers shouldn't classify these.
        // Returning Unknown keeps the contract simple if one slips through.
        if (ex is OperationCanceledException) return PollErrorKind.Unknown;

        if (ex is HttpRequestException hre)
        {
            if (hre.StatusCode.HasValue)
            {
                var byStatus = ClassifyHttpStatus((int)hre.StatusCode.Value);
                if (byStatus != PollErrorKind.Unknown) return byStatus;
            }

            var msg = hre.Message ?? string.Empty;

            // AdoRetryHelper throws "rate-limited (429) — wall-clock retry cap exceeded" — surface as Transient
            // so the next poll cycle simply retries; the cap is a shutdown guard, not a permanent failure.
            if (msg.Contains("wall-clock retry cap", StringComparison.OrdinalIgnoreCase))
                return PollErrorKind.Transient;

            if (ContainsAny(msg, "401", "Unauthorized")) return PollErrorKind.AuthRequired;
            if (ContainsAny(msg, "403", "Forbidden")) return PollErrorKind.AuthRequired;
            if (ContainsAny(msg, "404", "Not Found")) return PollErrorKind.Permanent;
            if (ContainsAny(msg, "429", "rate limit")) return PollErrorKind.Throttled;
            if (ContainsAny(msg, "500", "501", "502", "503", "504", "Server error")) return PollErrorKind.Transient;

            // No status code and no recognized signal — assume transient network problem rather than permanent.
            return PollErrorKind.Transient;
        }

        if (ex is TimeoutException) return PollErrorKind.Transient;
        if (ex is SocketException) return PollErrorKind.Transient;
        if (ex is IOException) return PollErrorKind.Transient;

        return PollErrorKind.Unknown;
    }

    public static PollErrorKind ClassifyHttpStatus(int statusCode)
    {
        return statusCode switch
        {
            401 or 403 => PollErrorKind.AuthRequired,
            404 => PollErrorKind.Permanent,
            429 => PollErrorKind.Throttled,
            >= 500 and <= 599 => PollErrorKind.Transient,
            _ => PollErrorKind.Unknown
        };
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
