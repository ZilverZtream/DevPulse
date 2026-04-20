using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class InboxRule
{
    public bool Enabled { get; set; } = true;
    public PrEventSource? EventSourceEquals { get; set; }
    public EventMeaning? EventMeaningEquals { get; set; }
    public string? AuthorEquals { get; set; }
    public string? AuthorContains { get; set; }
    public string? ExcludeAuthorContains { get; set; }
    public List<string>? MessageContainsAny { get; set; }
    public List<string>? MessageContainsAll { get; set; }
    public string? ExcludeMessageContains { get; set; }
    public string? RepositoryEquals { get; set; }
    public string? ProjectEquals { get; set; }
    public string? ExcludeRepositoryEquals { get; set; }
    public string? StatusEquals { get; set; }
}
