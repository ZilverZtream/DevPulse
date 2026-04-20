using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class EventNormalizer
{
    public EventMeaning DeriveCommentMeaning(string messageText, string currentUserCanonicalKey)
    {
        var mentionHandle = currentUserCanonicalKey.Contains('@')
            ? currentUserCanonicalKey.Split('@')[0]
            : currentUserCanonicalKey;
        if (!string.IsNullOrEmpty(mentionHandle) &&
            messageText.Contains($"@{mentionHandle}", StringComparison.OrdinalIgnoreCase))
            return EventMeaning.Mention;

        return EventMeaning.Comment;
    }

    public EventMeaning DeriveVoteMeaning(int vote) => vote switch
    {
        -10 => EventMeaning.Blocked,
        _ => EventMeaning.VoteChanged
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

    public string BuildCollapsedEventId(int prId, PrEventSource source, DateTimeOffset pollTime)
        => $"pr:{prId}:collapsed:{source}:poll:{pollTime:yyyyMMddHHmm}";
}
