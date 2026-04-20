using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAzureDevOpsClient
{
    Task<IReadOnlyList<PullRequestDto>> GetRelevantPullRequestsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PullRequestThreadDto>> GetPullRequestThreadsAsync(int prId, string repoId, CancellationToken ct = default);
}

public sealed class PullRequestDto
{
    public int PullRequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public IdentityRefDto CreatedBy { get; set; } = new();
    public IdentityRefDto? CompletedBy { get; set; }
    public List<ReviewerDto> Reviewers { get; set; } = [];
    public DateTimeOffset CreationDate { get; set; }
    public DateTimeOffset? ClosedDate { get; set; }
}

public sealed class PullRequestThreadDto
{
    public int Id { get; set; }
    public DateTimeOffset PublishedDate { get; set; }
    public DateTimeOffset LastUpdatedDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<CommentDto> Comments { get; set; } = [];
}

public sealed class CommentDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public IdentityRefDto Author { get; set; } = new();
    public DateTimeOffset PublishedDate { get; set; }
    public int ParentCommentId { get; set; }
}

public sealed class ReviewerDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string UniqueName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public int Vote { get; set; }
    public IdentityRefDto AsIdentityRef() => new() { DisplayName = DisplayName, UniqueName = UniqueName, Id = Id };
}

public sealed class IdentityRefDto
{
    public string DisplayName { get; set; } = string.Empty;
    public string UniqueName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}
