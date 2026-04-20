namespace DevPulse.Core.Models;

public sealed class AppSettings
{
    public string OrganizationUrl { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string RepositoryFilter { get; set; } = string.Empty;
    public string CurrentUserCanonicalKey { get; set; } = string.Empty;
    public string CurrentUserDisplayName { get; set; } = string.Empty;
    public int PrPollingIntervalMinutes { get; set; } = 5;
    public int WorkItemPollingIntervalMinutes { get; set; } = 10;
    public bool RefreshOnStartup { get; set; } = true;
    public string AreaPath { get; set; } = string.Empty;
    public string IterationPath { get; set; } = string.Empty;
    public List<string> SupportedWorkItemTypes { get; set; } = ["Feature", "Bug", "Task", "User Story"];
    public int MaxEventsPerInbox { get; set; } = 100;
    public int DebugLogRetentionCount { get; set; } = 500;
    public List<string> BotIdentityPatterns { get; set; } = [];
    public List<string> PoQaGroupCanonicalKeys { get; set; } = [];
    public string NeedsAttentionKeywordPackName { get; set; } = "needs-attention";
}
