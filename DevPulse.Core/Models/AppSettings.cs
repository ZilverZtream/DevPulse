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
    public int DebugLogRetentionCount { get; set; } = 500;
    public int DebugWindowDisplayCount { get; set; } = 500;
    public int PrThreadFetchParallelism { get; set; } = 4;
    public int PrLookbackDays { get; set; } = 30;
    public List<string> BotIdentityPatterns { get; set; } = ["bot", "coderabbit", "[bot]", "automation"];
    public List<string> PoQaGroupCanonicalKeys { get; set; } = [];
    public string NeedsAttentionKeywordPackName { get; set; } = "needs-attention";
    public string AiOutputRootPath { get; set; } = DefaultAiOutputRootPath();
    public bool HasCompletedFirstRun { get; set; }

    // Computed lazily so the resolved path tracks the actual user profile at first-run, not at
    // assembly load. Using SpecialFolder.UserProfile + "Documents" keeps the default reasonable on
    // localized Windows installs (Documents folder name doesn't matter — we anchor under profile).
    public static string DefaultAiOutputRootPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "DevPulse", "specs");
}
