using System.Security.Cryptography;
using System.Text;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class EventNormalizer
{
    public EventMeaning DeriveCommentMeaning(string messageText, string currentUserCanonicalKey, string currentUserDisplayName = "")
    {
        var atIdx = currentUserCanonicalKey.LastIndexOf('@');
        var mentionHandle = atIdx > 0 ? currentUserCanonicalKey[..atIdx] : currentUserCanonicalKey;

        if (!string.IsNullOrEmpty(mentionHandle) &&
            messageText.Contains($"@{mentionHandle}", StringComparison.OrdinalIgnoreCase))
            return EventMeaning.Mention;

        if (!string.IsNullOrEmpty(currentUserDisplayName) &&
            messageText.Contains($"@{currentUserDisplayName}", StringComparison.OrdinalIgnoreCase))
            return EventMeaning.Mention;

        return EventMeaning.Comment;
    }

    public EventMeaning DeriveVoteMeaning(int vote) => vote switch
    {
        10  => EventMeaning.VoteApproved,
        5   => EventMeaning.VoteApprovedWithSuggestions,
        -5  => EventMeaning.VoteWaiting,
        -10 => EventMeaning.Blocked,
        _   => EventMeaning.VoteChanged
    };

    public EventMeaning DeriveStatusMeaning(string status) => status.ToLowerInvariant() switch
    {
        "completed" => EventMeaning.Merged,
        "abandoned" => EventMeaning.Abandoned,
        _ => EventMeaning.Unknown
    };

    public string BuildCommentEventId(int prId, int threadId, int commentId)
        => $"pr:{prId}:thread:{threadId}:comment:{commentId}";

    public string BuildStatusEventId(int prId, string status, DateTimeOffset at)
        => $"pr:{prId}:status:{status}:at:{at:yyyyMMddHHmmss}";

    public string BuildReviewerAddedEventId(int prId, string reviewerId)
        => $"pr:{prId}:reviewer:{reviewerId}:added";

    public string BuildVoteEventId(int prId, string reviewerId, int vote, DateTimeOffset at)
        => $"pr:{prId}:reviewer:{reviewerId}:vote:{vote}:at:{at:yyyyMMddHHmmss}";

    public string BuildCollapsedEventId(int prId, PrEventSource source, IEnumerable<string> constituentIds)
    {
        var joined = string.Join("|", constituentIds.OrderBy(x => x, StringComparer.Ordinal));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        return $"pr:{prId}:collapsed:{source}:{hash}";
    }
}
