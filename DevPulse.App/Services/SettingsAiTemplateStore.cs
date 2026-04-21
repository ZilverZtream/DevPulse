using System.Text.Json;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Services;

public sealed class SettingsAiTemplateStore : IAiTemplateStore
{
    private readonly SettingsService _settings;
    private const string Key = "AiTemplates";

    public SettingsAiTemplateStore(SettingsService settings) => _settings = settings;

    public async Task<List<AiTemplate>> GetTemplatesAsync(CancellationToken ct = default)
    {
        var json = await _settings.GetRawSettingAsync(Key, ct);
        if (string.IsNullOrEmpty(json)) return DefaultTemplates();
        try
        {
            var list = JsonSerializer.Deserialize<List<AiTemplate>>(json, SharedJsonOptions.Settings);
            return list is { Count: > 0 } ? list : DefaultTemplates();
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "AiTemplates JSON corrupt; returning defaults");
            return DefaultTemplates();
        }
    }

    public async Task SaveTemplatesAsync(List<AiTemplate> templates, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(templates, SharedJsonOptions.Settings);
        await _settings.SetRawSettingAsync(Key, json, ct);
    }

    public async Task<AiTemplate?> GetDefaultTemplateForAsync(string workItemType, CancellationToken ct = default)
    {
        var list = await GetTemplatesAsync(ct);
        return list.FirstOrDefault(t => t.AppliesTo.Any(a => a.Equals(workItemType, StringComparison.OrdinalIgnoreCase)));
    }

    internal static List<AiTemplate> DefaultTemplates() =>
    [
        new AiTemplate
        {
            Id = "bug-default",
            Name = "Bug — default",
            AppliesTo = ["Bug"],
            RequiredHeaders = DefaultRequiredHeaders,
            PromptBody = BugPromptBody
        },
        new AiTemplate
        {
            Id = "userstory-default",
            Name = "User Story — default",
            AppliesTo = ["User Story"],
            RequiredHeaders = DefaultRequiredHeaders,
            PromptBody = UserStoryPromptBody
        },
        new AiTemplate
        {
            Id = "feature-default",
            Name = "Feature — default",
            AppliesTo = ["Feature"],
            RequiredHeaders = DefaultRequiredHeaders,
            PromptBody = FeaturePromptBody
        },
        new AiTemplate
        {
            Id = "task-default",
            Name = "Task — default",
            AppliesTo = ["Task"],
            RequiredHeaders = DefaultRequiredHeaders,
            PromptBody = TaskPromptBody
        }
    ];

    private static List<string> DefaultRequiredHeaders =>
    [
        "Context summary", "Functional requirements", "Acceptance criteria",
        "Edge cases", "Test plan", "Risks and dependencies"
    ];

    private const string BaseInstructions = """
        You are drafting an implementation spec for an Azure DevOps work item.
        Return ONLY markdown. Use EXACTLY these H2 section headings in this order:

        ## Context summary
        ## Functional requirements
        ## Acceptance criteria
        ## Edge cases
        ## Test plan
        ## Risks and dependencies

        Each section must have non-empty content. Use Given/When/Then for acceptance criteria.
        Do not wrap the response in code fences.
        """;

    private const string BugPromptBody = BaseInstructions + """


        --- WORK ITEM ---
        Type: Bug
        Title: {title}
        State: {state}
        Area: {areaPath}
        Iteration: {iterationPath}

        Description:
        {description}

        Acceptance (existing):
        {acceptanceCriteria}

        Focus the spec on reproduction, root-cause hypotheses, regression test, and blast radius.
        """;

    private const string UserStoryPromptBody = BaseInstructions + """


        --- WORK ITEM ---
        Type: User Story
        Title: {title}
        State: {state}
        Area: {areaPath}
        Iteration: {iterationPath}

        Description:
        {description}

        Acceptance (existing):
        {acceptanceCriteria}

        Emphasise user value, acceptance criteria in G/W/T, and integration points.
        """;

    private const string FeaturePromptBody = BaseInstructions + """


        --- WORK ITEM ---
        Type: Feature
        Title: {title}
        State: {state}
        Area: {areaPath}
        Iteration: {iterationPath}

        Description:
        {description}

        Acceptance (existing):
        {acceptanceCriteria}

        Treat as a multi-story epic. Call out sub-stories, phasing, and dependencies.
        """;

    private const string TaskPromptBody = BaseInstructions + """


        --- WORK ITEM ---
        Type: Task
        Title: {title}
        State: {state}
        Area: {areaPath}
        Iteration: {iterationPath}

        Description:
        {description}

        Acceptance (existing):
        {acceptanceCriteria}

        Keep scope tight — one tech change. Focus the test plan on unit coverage.
        """;
}
