namespace DevPulse.Core.Enums;

public enum DevOpsEventType
{
    Unknown = 0,
    PullRequestCreated = 1,
    PullRequestCompleted = 2,
    PullRequestAbandoned = 3,
    CommentAdded = 4,
    ThreadUpdated = 5,
    ReviewerAdded = 6,
    ReviewerVoteChanged = 7
}
