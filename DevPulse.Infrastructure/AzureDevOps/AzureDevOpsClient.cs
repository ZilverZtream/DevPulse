using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly HttpClient _http;
    private readonly string _orgUrl;
    private readonly string _project;
    private readonly string _repoFilter;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AzureDevOpsClient(HttpClient http, string orgUrl, string project, string repoFilter)
    {
        _http = http;
        _orgUrl = orgUrl.TrimEnd('/');
        _project = project;
        _repoFilter = repoFilter;
    }

    public async Task<IReadOnlyList<PullRequestDto>> GetRelevantPullRequestsAsync(CancellationToken ct = default)
    {
        const int pageSize = 200;
        const int maxPages = 5;
        var allPrs = new List<PullRequestDto>();
        bool reachedHardCap = false;

        for (int page = 0; page < maxPages; page++)
        {
            var skip = page * pageSize;
            var url = $"{_orgUrl}/{Uri.EscapeDataString(_project)}/_apis/git/pullrequests" +
                      $"?searchCriteria.status=all&$top={pageSize}&$skip={skip}&api-version={ApiVersions.PullRequests}";

            if (!string.IsNullOrWhiteSpace(_repoFilter))
                url += $"&searchCriteria.repositoryId={Uri.EscapeDataString(_repoFilter)}";

            var response = await AdoRetryHelper.GetWithRetryAsync(_http, url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<AdoListResponse<AdoPullRequest>>(body, JsonOpts);
            var pageItems = result?.Value;
            if (pageItems == null || pageItems.Count == 0) break;

            allPrs.AddRange(pageItems.Select(Map));
            if (pageItems.Count < pageSize) break;
            if (page == maxPages - 1) reachedHardCap = true;
        }

        if (reachedHardCap)
            Log.Warning("PR fetch reached the {Cap}-item hard cap; some PRs may have been skipped. Configure a repository filter to reduce scope.", pageSize * maxPages);

        return allPrs;
    }

    public async Task<IReadOnlyList<PullRequestThreadDto>> GetPullRequestThreadsAsync(int prId, string repoId, CancellationToken ct = default)
    {
        var url = $"{_orgUrl}/{Uri.EscapeDataString(_project)}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}" +
                  $"/pullrequests/{prId}/threads?api-version={ApiVersions.PullRequestThreads}";

        var response = await AdoRetryHelper.GetWithRetryAsync(_http, url, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<AdoListResponse<AdoThread>>(body, JsonOpts);
        return result?.Value?.Select(MapThread).ToList() ?? [];
    }

    private PullRequestDto Map(AdoPullRequest pr) => new()
    {
        PullRequestId = pr.PullRequestId,
        Title = pr.Title ?? string.Empty,
        Status = pr.Status ?? string.Empty,
        Description = pr.Description ?? string.Empty,
        Url = pr.Url ?? string.Empty,
        RepositoryId = pr.Repository?.Id ?? string.Empty,
        RepositoryName = pr.Repository?.Name ?? string.Empty,
        Project = _project,
        Organization = _orgUrl,
        CreatedBy = MapIdentity(pr.CreatedBy),
        CompletedBy = pr.CompletedBy != null ? MapIdentity(pr.CompletedBy) : null,
        Reviewers = pr.Reviewers?.Select(MapReviewer).ToList() ?? [],
        CreationDate = pr.CreationDate,
        ClosedDate = pr.ClosedDate
    };

    private static PullRequestThreadDto MapThread(AdoThread t) => new()
    {
        Id = t.Id,
        PublishedDate = t.PublishedDate,
        LastUpdatedDate = t.LastUpdatedDate,
        Status = t.Status ?? string.Empty,
        Comments = t.Comments?.Select(MapComment).ToList() ?? []
    };

    private static CommentDto MapComment(AdoComment c) => new()
    {
        Id = c.Id,
        Content = c.Content ?? string.Empty,
        Author = MapIdentity(c.Author),
        PublishedDate = c.PublishedDate,
        ParentCommentId = c.ParentCommentId
    };

    private static IdentityRefDto MapIdentity(AdoIdentityRef? i) => i == null ? new() : new()
    {
        DisplayName = i.DisplayName ?? string.Empty,
        UniqueName = i.UniqueName ?? string.Empty,
        Id = i.Id ?? string.Empty
    };

    private static ReviewerDto MapReviewer(AdoReviewer r) => new()
    {
        DisplayName = r.DisplayName ?? string.Empty,
        UniqueName = r.UniqueName ?? string.Empty,
        Id = r.Id ?? string.Empty,
        Vote = r.Vote
    };

    // ADO JSON DTOs
    private sealed class AdoListResponse<T> { public List<T>? Value { get; set; } }
    private sealed class AdoPullRequest
    {
        public int PullRequestId { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public string? Description { get; set; }
        public string? Url { get; set; }
        public AdoRepository? Repository { get; set; }
        public AdoIdentityRef? CreatedBy { get; set; }
        public AdoIdentityRef? CompletedBy { get; set; }
        public List<AdoReviewer>? Reviewers { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public DateTimeOffset? ClosedDate { get; set; }
    }
    private sealed class AdoRepository { public string? Id { get; set; } public string? Name { get; set; } }
    private sealed class AdoThread
    {
        public int Id { get; set; }
        public DateTimeOffset PublishedDate { get; set; }
        public DateTimeOffset LastUpdatedDate { get; set; }
        public string? Status { get; set; }
        public List<AdoComment>? Comments { get; set; }
    }
    private sealed class AdoComment
    {
        public int Id { get; set; }
        public string? Content { get; set; }
        public AdoIdentityRef? Author { get; set; }
        public DateTimeOffset PublishedDate { get; set; }
        public int ParentCommentId { get; set; }
    }
    private sealed class AdoIdentityRef
    {
        public string? DisplayName { get; set; }
        public string? UniqueName { get; set; }
        public string? Id { get; set; }
    }
    private sealed class AdoReviewer
    {
        public string? DisplayName { get; set; }
        public string? UniqueName { get; set; }
        public string? Id { get; set; }
        public int Vote { get; set; }
    }
}
