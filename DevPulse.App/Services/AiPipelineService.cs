using System.Diagnostics;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Services;

public sealed class AiPipelineService
{
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly IAiTemplateStore _templates;
    private readonly IAiSpecWriter _writer;
    private readonly IAiAttemptStore _attempts;
    private readonly string _outputRoot;
    private readonly string _projectSlug;
    private readonly Func<int, Task<WorkItem?>> _loadWorkItem;
    private readonly AiOutputValidator _validator = new();
    private readonly AiTemplateRenderer _renderer = new();

    public AiPipelineService(
        IEnumerable<IAiProvider> providers,
        IAiTemplateStore templates,
        IAiSpecWriter writer,
        IAiAttemptStore attempts,
        string outputRoot,
        string projectSlug,
        Func<int, Task<WorkItem?>> loadWorkItem)
    {
        _providers = providers.ToList();
        _templates = templates;
        _writer = writer;
        _attempts = attempts;
        _outputRoot = outputRoot;
        _projectSlug = projectSlug;
        _loadWorkItem = loadWorkItem;
    }

    public async Task<AiAttempt> GenerateAsync(int workItemId, string templateId, string providerId, CancellationToken ct)
    {
        var attempt = new AiAttempt
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = workItemId,
            Project = _projectSlug,
            TemplateId = templateId,
            ProviderId = providerId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            var wi = await _loadWorkItem(workItemId)
                ?? throw new InvalidOperationException($"Work item {workItemId} not found");
            var templates = await _templates.GetTemplatesAsync(ct);
            var template = templates.FirstOrDefault(t => t.Id == templateId)
                ?? throw new InvalidOperationException($"Template {templateId} not found");
            var provider = _providers.FirstOrDefault(p => p.Id == providerId)
                ?? throw new InvalidOperationException($"Provider {providerId} not found");

            var prompt = _renderer.Render(template.PromptBody, wi,
                description: wi.Title,
                acceptanceCriteria: "");

            // Invariant: prompt written BEFORE provider call so a crash mid-call leaves prompt on disk for debugging.
            await _writer.WriteAsync(_outputRoot, _projectSlug, workItemId, attempt.CreatedAtUtc,
                specMarkdown: string.Empty, promptMarkdown: prompt,
                attemptHistory: await _attempts.GetAttemptsForWorkItemAsync(workItemId, ct), ct);

            var sw = Stopwatch.StartNew();
            var result = await provider.GenerateAsync(
                new AiGenerateRequest(prompt, template.AppliesTo.FirstOrDefault() ?? "", TimeSpan.FromSeconds(90)), ct);
            attempt.DurationMs = (int)sw.ElapsedMilliseconds;
            attempt.Model = result.ModelUsed;
            attempt.TokensIn = result.TokensIn;
            attempt.TokensOut = result.TokensOut;

            var v = _validator.Validate(result.Markdown, template.RequiredHeaders);
            attempt.ValidationPassed = v.IsValid;
            attempt.MissingSections = [.. v.MissingHeaders.Concat(v.EmptySections).Distinct()];
            attempt.Status = v.IsValid ? AiAttemptStatus.Success : AiAttemptStatus.ValidationFailed;

            var history = (await _attempts.GetAttemptsForWorkItemAsync(workItemId, ct)).Concat([attempt]).ToList();
            var paths = await _writer.WriteAsync(_outputRoot, _projectSlug, workItemId, attempt.CreatedAtUtc,
                specMarkdown: result.Markdown, promptMarkdown: prompt, history, ct);
            attempt.SpecFilePath = paths.SpecPath;
            attempt.PromptFilePath = paths.PromptPath;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            attempt.Status = AiAttemptStatus.Timeout;
            attempt.ErrorMessage = "Cancelled by user";
        }
        catch (TimeoutException ex)
        {
            attempt.Status = AiAttemptStatus.Timeout;
            attempt.ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            attempt.Status = AiAttemptStatus.ProviderError;
            attempt.ErrorMessage = ex.Message;
            Log.Warning(ex, "AI pipeline failed for work item {WorkItemId}", workItemId);
        }

        await _attempts.RecordAttemptAsync(attempt, ct);
        return attempt;
    }
}
