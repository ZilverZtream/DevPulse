# AI Action Pipeline Implementation Plan — Part 2 (App layer, UI, wire-up)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Continues Part 1.

**Prerequisite:** Part 1 (`2026-04-21-ai-action-pipeline.md`) complete — Core enums/models/interfaces, Schema v2, SqliteAiAttemptStore, FilesystemSpecWriter, OpenRouterProvider, ClaudeCliProvider all landed and green.

**Goal of Part 2:** Settings plumbing, pipeline orchestrator, WinForms UI (dialog + review form + SettingsForm AI tab), BoardForm context menu, TrayApplicationContext wire-up.

---

## Phase 7 — Settings & templates

### Task 13: Extend `AppSettings` with AI fields

**Files:**
- Modify: `DevPulse.Core/Models/AppSettings.cs`

- [ ] **Step 1: Add new fields**

Append to `AppSettings`:

```csharp
public string AiOutputRootPath { get; set; } = @"C:\devops";
```

- [ ] **Step 2: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add DevPulse.Core/Models/AppSettings.cs
git commit -m "feat(ai): AppSettings.AiOutputRootPath with C:\\devops default"
```

---

### Task 14: `SettingsAiTemplateStore` — IAiTemplateStore backed by SettingsService

**Files:**
- Create: `DevPulse.App/Services/SettingsAiTemplateStore.cs`
- Create: `DevPulse.Tests/SettingsAiTemplateStoreTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SettingsAiTemplateStoreTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;
    private SettingsService _settings = null!;
    private SettingsAiTemplateStore _sut = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"tpl-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
        _settings = new SettingsService(_store);
        _sut = new SettingsAiTemplateStore(_settings);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task GetTemplates_EmptyStore_ReturnsShippedDefaults()
    {
        var list = await _sut.GetTemplatesAsync();
        list.Should().NotBeEmpty();
        list.Select(t => t.Id).Should().Contain(new[] { "bug-default", "userstory-default", "feature-default", "task-default" });
    }

    [Fact]
    public async Task SaveThenGet_RoundTrips()
    {
        var mine = new AiTemplate
        {
            Id = "custom",
            Name = "My Custom",
            AppliesTo = ["Bug"],
            RequiredHeaders = ["Header A", "Header B"],
            PromptBody = "Body with {title}"
        };
        await _sut.SaveTemplatesAsync([mine]);
        var list = await _sut.GetTemplatesAsync();
        list.Should().ContainSingle().Which.Id.Should().Be("custom");
    }

    [Fact]
    public async Task GetDefaultTemplateFor_MatchesByWorkItemType()
    {
        var t = await _sut.GetDefaultTemplateForAsync("Bug");
        t.Should().NotBeNull();
        t!.AppliesTo.Should().Contain("Bug");
    }

    [Fact]
    public async Task GetDefaultTemplateFor_UnknownType_ReturnsNull()
    {
        var t = await _sut.GetDefaultTemplateForAsync("Unicorn");
        t.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~SettingsAiTemplateStoreTests"`
Expected: build fails.

- [ ] **Step 3: Implement the store with shipped defaults**

Create `DevPulse.App/Services/SettingsAiTemplateStore.cs`:

```csharp
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
            RequiredHeaders = ["Context summary", "Functional requirements", "Acceptance criteria",
                               "Edge cases", "Test plan", "Risks and dependencies"],
            PromptBody = BugPromptBody
        },
        new AiTemplate
        {
            Id = "userstory-default",
            Name = "User Story — default",
            AppliesTo = ["User Story"],
            RequiredHeaders = ["Context summary", "Functional requirements", "Acceptance criteria",
                               "Edge cases", "Test plan", "Risks and dependencies"],
            PromptBody = UserStoryPromptBody
        },
        new AiTemplate
        {
            Id = "feature-default",
            Name = "Feature — default",
            AppliesTo = ["Feature"],
            RequiredHeaders = ["Context summary", "Functional requirements", "Acceptance criteria",
                               "Edge cases", "Test plan", "Risks and dependencies"],
            PromptBody = FeaturePromptBody
        },
        new AiTemplate
        {
            Id = "task-default",
            Name = "Task — default",
            AppliesTo = ["Task"],
            RequiredHeaders = ["Context summary", "Functional requirements", "Acceptance criteria",
                               "Edge cases", "Test plan", "Risks and dependencies"],
            PromptBody = TaskPromptBody
        }
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
```

- [ ] **Step 4: Add `GetRawSettingAsync`/`SetRawSettingAsync` to `SettingsService`**

Edit `DevPulse.App/Services/SettingsService.cs` — add near the other public getters:

```csharp
public Task<string?> GetRawSettingAsync(string key, CancellationToken ct = default)
    => _store.GetSettingAsync(key, ct);

public Task SetRawSettingAsync(string key, string value, CancellationToken ct = default)
    => _store.SetSettingAsync(key, value, ct);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~SettingsAiTemplateStoreTests"`
Expected: `Passed! - Failed: 0, Passed: 4`

- [ ] **Step 6: Commit**

```bash
git add DevPulse.App/Services/SettingsAiTemplateStore.cs DevPulse.App/Services/SettingsService.cs DevPulse.Tests/SettingsAiTemplateStoreTests.cs
git commit -m "feat(ai): SettingsAiTemplateStore with four shipped default templates"
```

---

### Task 15: `SettingsService.SaveAiConfigAsync` — atomic KV batch write

**Files:**
- Modify: `DevPulse.Core/Interfaces/IStateStore.cs`
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs`
- Modify: `DevPulse.App/Services/SettingsService.cs`

- [ ] **Step 1: Add batch KV method to IStateStore**

In `IStateStore`:

```csharp
Task SetSettingsBatchAsync(IReadOnlyList<(string Key, string Value)> entries, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in SqliteStateStore**

Add near the existing `SetSettingAsync`:

```csharp
public async Task SetSettingsBatchAsync(IReadOnlyList<(string Key, string Value)> entries, CancellationToken ct = default)
{
    if (entries.Count == 0) return;
    await _lock.WaitAsync(ct);
    try
    {
        await using var tx = await _conn.BeginTransactionAsync(ct);
        foreach (var (key, value) in entries)
        {
            await using var cmd = _conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "INSERT INTO kv_settings VALUES(@k,@v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@v", value);
            await NonQueryRetryAsync(cmd, ct);
        }
        await tx.CommitAsync(ct);
    }
    finally { _lock.Release(); }
}
```

- [ ] **Step 3: Add `SaveAiConfigAsync` to SettingsService**

In `SettingsService.cs`:

```csharp
public async Task SaveAiConfigAsync(
    List<AiProviderProfile> providers,
    List<AiTemplate> templates,
    CancellationToken ct = default)
{
    var entries = new List<(string, string)>
    {
        ("AiProviderProfiles", JsonSerializer.Serialize(providers, Json)),
        ("AiTemplates", JsonSerializer.Serialize(templates, Json))
    };
    await _store.SetSettingsBatchAsync(entries, ct);
}

public async Task<List<AiProviderProfile>> GetAiProviderProfilesAsync(CancellationToken ct = default)
{
    var json = await _store.GetSettingAsync("AiProviderProfiles", ct);
    if (string.IsNullOrEmpty(json)) return [];
    try { return JsonSerializer.Deserialize<List<AiProviderProfile>>(json, Json) ?? []; }
    catch (JsonException ex) { Log.Warning(ex, "AiProviderProfiles JSON corrupt"); return []; }
}
```

- [ ] **Step 4: Build + run tests**

Run: `dotnet build --nologo` then `dotnet test --nologo --no-build`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Interfaces/IStateStore.cs DevPulse.Infrastructure/Persistence/SqliteStateStore.cs DevPulse.App/Services/SettingsService.cs
git commit -m "feat(ai): SaveAiConfigAsync writes provider profiles + templates atomically"
```

---

## Phase 8 — Pipeline orchestrator (TDD)

### Task 16: `AiPipelineService` — orchestrator with full test coverage

**Files:**
- Create: `DevPulse.App/Services/AiPipelineService.cs`
- Create: `DevPulse.Tests/AiPipelineServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using DevPulse.App.Services;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using FluentAssertions;

namespace DevPulse.Tests;

public class AiPipelineServiceTests
{
    private sealed class FakeProvider : IAiProvider
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public AiProviderKind Kind => AiProviderKind.Http;
        public AiDataPolicy DataPolicy => AiDataPolicy.Local;
        public Func<AiGenerateRequest, Task<AiGenerateResult>> OnGenerate = _ =>
            Task.FromResult(new AiGenerateResult("## Context summary\n x\n## Functional requirements\n y\n## Acceptance criteria\n z\n## Edge cases\n a\n## Test plan\n b\n## Risks and dependencies\n c",
                "fake-model", 10, 20, TimeSpan.FromMilliseconds(100), null));
        public Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default) => Task.FromResult(new AiHealthResult(true, null));
        public Task<AiGenerateResult> GenerateAsync(AiGenerateRequest r, CancellationToken ct = default) => OnGenerate(r);
    }

    private sealed class FakeWriter : IAiSpecWriter
    {
        public List<string> SpecCalls = [];
        public List<string> PromptCalls = [];
        public Task<AiFilePaths> WriteAsync(string root, string slug, int id, DateTimeOffset ts,
            string spec, string prompt, IReadOnlyList<AiAttempt> history, CancellationToken ct = default)
        {
            SpecCalls.Add(spec);
            PromptCalls.Add(prompt);
            return Task.FromResult(new AiFilePaths("spec.md", "prompt.md", "meta.json"));
        }
    }

    private sealed class FakeAttemptStore : IAiAttemptStore
    {
        public List<AiAttempt> Recorded = [];
        public Task RecordAttemptAsync(AiAttempt a, CancellationToken ct = default) { Recorded.Add(a); return Task.CompletedTask; }
        public Task<IReadOnlyList<AiAttempt>> GetAttemptsForWorkItemAsync(int id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AiAttempt>>(Recorded.Where(a => a.WorkItemId == id).ToList());
    }

    private sealed class FakeTemplateStore : IAiTemplateStore
    {
        public AiTemplate Template { get; set; } = new()
        {
            Id = "t1", Name = "T",
            AppliesTo = ["Bug"],
            RequiredHeaders = ["Context summary", "Functional requirements", "Acceptance criteria",
                               "Edge cases", "Test plan", "Risks and dependencies"],
            PromptBody = "Draft for {title}"
        };
        public Task<List<AiTemplate>> GetTemplatesAsync(CancellationToken ct = default) => Task.FromResult(new List<AiTemplate> { Template });
        public Task SaveTemplatesAsync(List<AiTemplate> templates, CancellationToken ct = default) => Task.CompletedTask;
        public Task<AiTemplate?> GetDefaultTemplateForAsync(string wit, CancellationToken ct = default) => Task.FromResult<AiTemplate?>(Template);
    }

    private static WorkItem Wi() => new() { Id = 1, Title = "t", Type = WorkItemType.Bug, State = "New" };

    private static AiPipelineService Sut(FakeProvider p, FakeWriter w, FakeAttemptStore a, FakeTemplateStore ts)
        => new(new[] { (IAiProvider)p }, ts, w, a, "C:\\devops", "Proj", _ => Task.FromResult<WorkItem?>(Wi()));

    [Fact]
    public async Task GenerateAsync_HappyPath_RecordsSuccessAttempt()
    {
        var p = new FakeProvider(); var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(workItemId: 1, templateId: "t1", providerId: "fake", ct: default);

        result.Status.Should().Be(AiAttemptStatus.Success);
        result.ValidationPassed.Should().BeTrue();
        a.Recorded.Should().HaveCount(1);
        a.Recorded[0].Status.Should().Be(AiAttemptStatus.Success);
    }

    [Fact]
    public async Task GenerateAsync_ProviderThrows_RecordsProviderErrorWithMessage()
    {
        var p = new FakeProvider { OnGenerate = _ => throw new HttpRequestException("boom") };
        var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(1, "t1", "fake", default);

        result.Status.Should().Be(AiAttemptStatus.ProviderError);
        result.ErrorMessage.Should().Contain("boom");
        a.Recorded[0].Status.Should().Be(AiAttemptStatus.ProviderError);
    }

    [Fact]
    public async Task GenerateAsync_ValidationFails_RecordsValidationFailed()
    {
        var p = new FakeProvider { OnGenerate = _ => Task.FromResult(new AiGenerateResult("## only one header\nx", "m", 0, 0, TimeSpan.Zero, null)) };
        var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(1, "t1", "fake", default);

        result.Status.Should().Be(AiAttemptStatus.ValidationFailed);
        result.MissingSections.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_Timeout_RecordsTimeout()
    {
        var p = new FakeProvider { OnGenerate = _ => throw new TimeoutException("over") };
        var w = new FakeWriter(); var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();
        var sut = Sut(p, w, a, ts);

        var result = await sut.GenerateAsync(1, "t1", "fake", default);

        result.Status.Should().Be(AiAttemptStatus.Timeout);
    }

    [Fact]
    public async Task GenerateAsync_WritesPromptBeforeProviderCall()
    {
        int order = 0;
        int promptWriteOrder = 0, providerCallOrder = 0;
        var w = new FakeWriter();
        var p = new FakeProvider
        {
            OnGenerate = _ =>
            {
                providerCallOrder = ++order;
                return Task.FromResult(new AiGenerateResult("## Context summary\nx\n## Functional requirements\ny\n## Acceptance criteria\nz\n## Edge cases\na\n## Test plan\nb\n## Risks and dependencies\nc", "m", 0, 0, TimeSpan.Zero, null));
            }
        };
        var a = new FakeAttemptStore(); var ts = new FakeTemplateStore();

        // wrap writer to record prompt order
        var sw = new OrderingWriter(w, () => promptWriteOrder = ++order);
        var sut = new AiPipelineService(new[] { (IAiProvider)p }, ts, sw, a, "C:\\devops", "Proj", _ => Task.FromResult<WorkItem?>(Wi()));

        await sut.GenerateAsync(1, "t1", "fake", default);

        promptWriteOrder.Should().BeLessThan(providerCallOrder);
    }

    private sealed class OrderingWriter : IAiSpecWriter
    {
        private readonly IAiSpecWriter _inner; private readonly Action _onWrite;
        public OrderingWriter(IAiSpecWriter inner, Action onWrite) { _inner = inner; _onWrite = onWrite; }
        public Task<AiFilePaths> WriteAsync(string root, string slug, int id, DateTimeOffset ts,
            string spec, string prompt, IReadOnlyList<AiAttempt> history, CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(prompt) && string.IsNullOrEmpty(spec)) _onWrite();
            return _inner.WriteAsync(root, slug, id, ts, spec, prompt, history, ct);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AiPipelineServiceTests"`
Expected: build fails — `AiPipelineService` not defined.

- [ ] **Step 3: Implement AiPipelineService**

Create `DevPulse.App/Services/AiPipelineService.cs`:

```csharp
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
                description: wi.Title /* description unavailable here; caller can extend */,
                acceptanceCriteria: "");

            // 1) Write prompt BEFORE provider call (debuggability invariant)
            await _writer.WriteAsync(_outputRoot, _projectSlug, workItemId, attempt.CreatedAtUtc,
                specMarkdown: string.Empty, promptMarkdown: prompt,
                attemptHistory: await _attempts.GetAttemptsForWorkItemAsync(workItemId, ct), ct);

            // 2) Call provider
            var sw = Stopwatch.StartNew();
            var result = await provider.GenerateAsync(
                new AiGenerateRequest(prompt, template.AppliesTo.FirstOrDefault() ?? "", TimeSpan.FromSeconds(90)), ct);
            attempt.DurationMs = (int)sw.ElapsedMilliseconds;
            attempt.Model = result.ModelUsed;
            attempt.TokensIn = result.TokensIn;
            attempt.TokensOut = result.TokensOut;

            // 3) Validate
            var v = _validator.Validate(result.Markdown, template.RequiredHeaders);
            attempt.ValidationPassed = v.IsValid;
            attempt.MissingSections = [.. v.MissingHeaders.Concat(v.EmptySections).Distinct()];
            attempt.Status = v.IsValid ? AiAttemptStatus.Success : AiAttemptStatus.ValidationFailed;

            // 4) Write spec (always)
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AiPipelineServiceTests"`
Expected: 5 pass.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.App/Services/AiPipelineService.cs DevPulse.Tests/AiPipelineServiceTests.cs
git commit -m "feat(ai): AiPipelineService orchestrator with TDD coverage for all status branches"
```

---

## Phase 9 — UI

### Task 17: Simple Markdown→RichTextBox renderer

**Files:**
- Create: `DevPulse.App/UI/MarkdownRenderer.cs`
- Create: `DevPulse.Tests/MarkdownRendererTests.cs`

The renderer targets only the AI-spec subset: H1–H4, paragraphs, ordered/unordered lists, inline `code`, fenced code blocks, bold/italic. Not a general-purpose renderer.

- [ ] **Step 1: Write failing tests**

```csharp
using DevPulse.App.UI;
using FluentAssertions;

namespace DevPulse.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void ToRtf_HeaderProducesBoldLine()
    {
        var rtf = MarkdownRenderer.ToRtf("## Heading");
        rtf.Should().Contain("\\b");
        rtf.Should().Contain("Heading");
    }

    [Fact]
    public void ToRtf_ParagraphPreservesText()
    {
        var rtf = MarkdownRenderer.ToRtf("Just a paragraph.");
        rtf.Should().Contain("Just a paragraph.");
    }

    [Fact]
    public void ToRtf_UnorderedListRendered()
    {
        var rtf = MarkdownRenderer.ToRtf("- item one\n- item two");
        rtf.Should().Contain("item one");
        rtf.Should().Contain("item two");
    }

    [Fact]
    public void ToRtf_EscapesRtfControlChars()
    {
        var rtf = MarkdownRenderer.ToRtf("a\\b{c}d");
        rtf.Should().Contain("\\\\");
        rtf.Should().Contain("\\{");
        rtf.Should().Contain("\\}");
    }

    [Fact]
    public void ToRtf_EmptyReturnsEmptyRtf()
    {
        var rtf = MarkdownRenderer.ToRtf("");
        rtf.Should().StartWith("{\\rtf1");
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MarkdownRendererTests"`
Expected: build fails.

- [ ] **Step 3: Implement MarkdownRenderer**

Create `DevPulse.App/UI/MarkdownRenderer.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace DevPulse.App.UI;

public static class MarkdownRenderer
{
    public static string ToRtf(string markdown)
    {
        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}{\f1 Consolas;}}\fs20 ");

        if (string.IsNullOrEmpty(markdown))
        {
            sb.Append('}');
            return sb.ToString();
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        bool inFence = false;

        foreach (var raw in lines)
        {
            if (raw.StartsWith("```"))
            {
                inFence = !inFence;
                sb.Append(inFence ? @"\f1\fs18 " : @"\f0\fs20 ");
                sb.Append(@"\par ");
                continue;
            }

            if (inFence)
            {
                sb.Append(Escape(raw));
                sb.Append(@"\par ");
                continue;
            }

            var h = Regex.Match(raw, @"^(#{1,4})\s+(.*)$");
            if (h.Success)
            {
                var level = h.Groups[1].Length;
                var size = level switch { 1 => 32, 2 => 26, 3 => 22, _ => 20 };
                sb.Append($@"\b\fs{size} ");
                sb.Append(Escape(h.Groups[2].Value));
                sb.Append(@"\b0\fs20\par ");
                continue;
            }

            var ul = Regex.Match(raw, @"^\s*[-*]\s+(.*)$");
            if (ul.Success)
            {
                sb.Append(@"\bullet\tab ");
                sb.Append(Escape(ul.Groups[1].Value));
                sb.Append(@"\par ");
                continue;
            }

            var ol = Regex.Match(raw, @"^\s*\d+\.\s+(.*)$");
            if (ol.Success)
            {
                sb.Append(Escape(ol.Value.TrimStart()));
                sb.Append(@"\par ");
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                sb.Append(@"\par ");
                continue;
            }

            sb.Append(Escape(raw));
            sb.Append(@"\par ");
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '{': sb.Append(@"\{"); break;
                case '}': sb.Append(@"\}"); break;
                default:
                    if (c > 127) sb.Append($@"\u{(int)c}?");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MarkdownRendererTests"`
Expected: 5 pass.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.App/UI/MarkdownRenderer.cs DevPulse.Tests/MarkdownRendererTests.cs
git commit -m "feat(ai): minimal markdown→RTF renderer for the AI spec subset"
```

---

### Task 18: `AiGenerateDialog` WinForm

**Files:**
- Create: `DevPulse.App/Forms/AiGenerateDialog.cs`
- Create: `DevPulse.App/Forms/AiGenerateDialog.Designer.cs`

This is UI scaffolding only — no business logic that isn't already covered by `AiPipelineService`. No unit tests for the form (WinForms snapshot tests aren't worth the tooling cost in this project).

- [ ] **Step 1: Create Designer file**

`DevPulse.App/Forms/AiGenerateDialog.Designer.cs`:

```csharp
namespace DevPulse.App.Forms;

partial class AiGenerateDialog
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label _lblHeader;
    private System.Windows.Forms.Label _lblTemplate;
    private System.Windows.Forms.ComboBox _cboTemplate;
    private System.Windows.Forms.Label _lblProvider;
    private System.Windows.Forms.ComboBox _cboProvider;
    private System.Windows.Forms.Label _lblModel;
    private System.Windows.Forms.TextBox _txtModel;
    private System.Windows.Forms.Label _lblWarning;
    private System.Windows.Forms.Button _btnGenerate;
    private System.Windows.Forms.Button _btnCancel;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _lblHeader = new System.Windows.Forms.Label();
        _lblTemplate = new System.Windows.Forms.Label();
        _cboTemplate = new System.Windows.Forms.ComboBox();
        _lblProvider = new System.Windows.Forms.Label();
        _cboProvider = new System.Windows.Forms.ComboBox();
        _lblModel = new System.Windows.Forms.Label();
        _txtModel = new System.Windows.Forms.TextBox();
        _lblWarning = new System.Windows.Forms.Label();
        _btnGenerate = new System.Windows.Forms.Button();
        _btnCancel = new System.Windows.Forms.Button();

        SuspendLayout();

        _lblHeader.Location = new System.Drawing.Point(16, 12);
        _lblHeader.Size = new System.Drawing.Size(380, 40);
        _lblHeader.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _lblHeader.Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold);

        _lblTemplate.Location = new System.Drawing.Point(16, 60);
        _lblTemplate.Size = new System.Drawing.Size(80, 22);
        _lblTemplate.Text = "Template:";
        _lblTemplate.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);

        _cboTemplate.Location = new System.Drawing.Point(100, 58);
        _cboTemplate.Size = new System.Drawing.Size(296, 22);
        _cboTemplate.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;

        _lblProvider.Location = new System.Drawing.Point(16, 92);
        _lblProvider.Size = new System.Drawing.Size(80, 22);
        _lblProvider.Text = "Provider:";
        _lblProvider.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);

        _cboProvider.Location = new System.Drawing.Point(100, 90);
        _cboProvider.Size = new System.Drawing.Size(296, 22);
        _cboProvider.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        _cboProvider.SelectedIndexChanged += new System.EventHandler(CboProvider_SelectedIndexChanged);

        _lblModel.Location = new System.Drawing.Point(16, 124);
        _lblModel.Size = new System.Drawing.Size(80, 22);
        _lblModel.Text = "Model:";
        _lblModel.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);

        _txtModel.Location = new System.Drawing.Point(100, 122);
        _txtModel.Size = new System.Drawing.Size(296, 22);

        _lblWarning.Location = new System.Drawing.Point(16, 156);
        _lblWarning.Size = new System.Drawing.Size(380, 40);
        _lblWarning.ForeColor = System.Drawing.Color.FromArgb(220, 120, 120);
        _lblWarning.Visible = false;

        _btnGenerate.Location = new System.Drawing.Point(216, 212);
        _btnGenerate.Size = new System.Drawing.Size(90, 28);
        _btnGenerate.Text = "Generate";
        _btnGenerate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnGenerate.BackColor = System.Drawing.Color.FromArgb(60, 100, 160);
        _btnGenerate.ForeColor = System.Drawing.Color.White;
        _btnGenerate.Click += new System.EventHandler(BtnGenerate_Click);

        _btnCancel.Location = new System.Drawing.Point(312, 212);
        _btnCancel.Size = new System.Drawing.Size(80, 28);
        _btnCancel.Text = "Cancel";
        _btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;

        ClientSize = new System.Drawing.Size(420, 260);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        Font = new System.Drawing.Font("Segoe UI", 9f);
        Text = "DevPulse — Draft spec with AI";
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        AcceptButton = _btnGenerate;
        CancelButton = _btnCancel;

        Controls.Add(_lblHeader);
        Controls.Add(_lblTemplate); Controls.Add(_cboTemplate);
        Controls.Add(_lblProvider); Controls.Add(_cboProvider);
        Controls.Add(_lblModel); Controls.Add(_txtModel);
        Controls.Add(_lblWarning);
        Controls.Add(_btnGenerate); Controls.Add(_btnCancel);

        ResumeLayout(false);
    }
}
```

- [ ] **Step 2: Create the form code-behind**

`DevPulse.App/Forms/AiGenerateDialog.cs`:

```csharp
using DevPulse.App.Services;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class AiGenerateDialog : Form
{
    private readonly AiPipelineService _pipeline;
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly IReadOnlyList<AiTemplate> _templates;
    private readonly WorkItem _workItem;
    private readonly CancellationTokenSource _cts = new();

    public AiAttempt? Result { get; private set; }

    public AiGenerateDialog(
        AiPipelineService pipeline,
        IReadOnlyList<IAiProvider> providers,
        IReadOnlyList<AiTemplate> templates,
        WorkItem workItem)
    {
        _pipeline = pipeline;
        _providers = providers;
        _templates = templates;
        _workItem = workItem;
        InitializeComponent();
        _lblHeader.Text = $"Work item #{workItem.Id} — {workItem.Title}";
        PopulateTemplates();
        PopulateProviders();
        FormClosing += (_, _) => _cts.Cancel();
    }

    private void PopulateTemplates()
    {
        _cboTemplate.Items.Clear();
        foreach (var t in _templates)
            _cboTemplate.Items.Add(new TemplateRow(t));
        // pre-select by work item type
        var defaultIdx = _templates.ToList().FindIndex(t =>
            t.AppliesTo.Any(a => a.Equals(_workItem.Type.ToString(), StringComparison.OrdinalIgnoreCase)));
        if (defaultIdx >= 0) _cboTemplate.SelectedIndex = defaultIdx;
        else if (_cboTemplate.Items.Count > 0) _cboTemplate.SelectedIndex = 0;
    }

    private void PopulateProviders()
    {
        _cboProvider.Items.Clear();
        foreach (var p in _providers)
            _cboProvider.Items.Add(new ProviderRow(p));
        if (_cboProvider.Items.Count > 0) _cboProvider.SelectedIndex = 0;
    }

    private void CboProvider_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboProvider.SelectedItem is not ProviderRow row) return;
        if (row.Provider.DataPolicy == AiDataPolicy.Cloud)
        {
            _lblWarning.Text = $"This will send the work item title, description, and your prompt to {row.Provider.DisplayName}. Review your org's data policy.";
            _lblWarning.Visible = true;
        }
        else _lblWarning.Visible = false;
    }

    private async void BtnGenerate_Click(object? sender, EventArgs e)
    {
        if (_cboTemplate.SelectedItem is not TemplateRow tpl) return;
        if (_cboProvider.SelectedItem is not ProviderRow prov) return;

        _btnGenerate.Enabled = false;
        _btnGenerate.Text = "Generating…";
        _cboTemplate.Enabled = false;
        _cboProvider.Enabled = false;

        try
        {
            Result = await _pipeline.GenerateAsync(_workItem.Id, tpl.Template.Id, prov.Provider.Id, _cts.Token);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI generate dialog failed");
            MessageBox.Show($"Generation failed: {ex.Message}", "DevPulse",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnGenerate.Enabled = true;
            _btnGenerate.Text = "Generate";
            _cboTemplate.Enabled = true;
            _cboProvider.Enabled = true;
        }
    }

    private sealed record TemplateRow(AiTemplate Template)
    {
        public override string ToString() => Template.Name;
    }

    private sealed record ProviderRow(IAiProvider Provider)
    {
        public override string ToString()
        {
            var tag = Provider.DataPolicy == AiDataPolicy.Cloud ? "[CLOUD]" : "[LOCAL]";
            return $"{tag} {Provider.DisplayName}";
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Forms/AiGenerateDialog.cs DevPulse.App/Forms/AiGenerateDialog.Designer.cs
git commit -m "feat(ai): AiGenerateDialog — template/provider picker with cloud warning"
```

---

### Task 19: `AiReviewForm` WinForm

**Files:**
- Create: `DevPulse.App/Forms/AiReviewForm.cs`
- Create: `DevPulse.App/Forms/AiReviewForm.Designer.cs`

- [ ] **Step 1: Create Designer**

`DevPulse.App/Forms/AiReviewForm.Designer.cs`:

```csharp
namespace DevPulse.App.Forms;

partial class AiReviewForm
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label _lblStatus;
    private System.Windows.Forms.ListBox _lstHistory;
    private System.Windows.Forms.RichTextBox _rtfSpec;
    private System.Windows.Forms.Label _lblMetadata;
    private System.Windows.Forms.Button _btnRegenerate;
    private System.Windows.Forms.Button _btnCopy;
    private System.Windows.Forms.Button _btnOpenFolder;
    private System.Windows.Forms.Button _btnClose;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        _lblStatus = new System.Windows.Forms.Label();
        _lstHistory = new System.Windows.Forms.ListBox();
        _rtfSpec = new System.Windows.Forms.RichTextBox();
        _lblMetadata = new System.Windows.Forms.Label();
        _btnRegenerate = new System.Windows.Forms.Button();
        _btnCopy = new System.Windows.Forms.Button();
        _btnOpenFolder = new System.Windows.Forms.Button();
        _btnClose = new System.Windows.Forms.Button();

        SuspendLayout();

        _lblStatus.Dock = System.Windows.Forms.DockStyle.Top;
        _lblStatus.Height = 40;
        _lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
        _lblStatus.Font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold);

        _lstHistory.Dock = System.Windows.Forms.DockStyle.Left;
        _lstHistory.Width = 220;
        _lstHistory.BackColor = System.Drawing.Color.FromArgb(36, 36, 52);
        _lstHistory.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _lstHistory.BorderStyle = System.Windows.Forms.BorderStyle.None;
        _lstHistory.SelectedIndexChanged += new System.EventHandler(LstHistory_SelectedIndexChanged);

        _rtfSpec.Dock = System.Windows.Forms.DockStyle.Fill;
        _rtfSpec.ReadOnly = true;
        _rtfSpec.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        _rtfSpec.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        _rtfSpec.BorderStyle = System.Windows.Forms.BorderStyle.None;

        _lblMetadata.Dock = System.Windows.Forms.DockStyle.Right;
        _lblMetadata.Width = 220;
        _lblMetadata.Padding = new System.Windows.Forms.Padding(8);
        _lblMetadata.ForeColor = System.Drawing.Color.FromArgb(180, 180, 200);
        _lblMetadata.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);

        var toolbar = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Bottom,
            Height = 40,
            BackColor = System.Drawing.Color.FromArgb(36, 36, 52),
            Padding = new System.Windows.Forms.Padding(8, 6, 8, 6)
        };

        _btnRegenerate.Text = "Regenerate";
        _btnRegenerate.Width = 100;
        _btnRegenerate.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnRegenerate.BackColor = System.Drawing.Color.FromArgb(60, 100, 160);
        _btnRegenerate.ForeColor = System.Drawing.Color.White;
        _btnRegenerate.Click += new System.EventHandler(BtnRegenerate_Click);

        _btnCopy.Text = "Copy markdown";
        _btnCopy.Width = 120;
        _btnCopy.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnCopy.Click += new System.EventHandler(BtnCopy_Click);

        _btnOpenFolder.Text = "Open folder";
        _btnOpenFolder.Width = 100;
        _btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnOpenFolder.Click += new System.EventHandler(BtnOpenFolder_Click);

        _btnClose.Text = "Close";
        _btnClose.Width = 80;
        _btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
        _btnClose.Click += (_, _) => Close();

        toolbar.Controls.Add(_btnRegenerate);
        toolbar.Controls.Add(_btnCopy);
        toolbar.Controls.Add(_btnOpenFolder);
        toolbar.Controls.Add(_btnClose);

        ClientSize = new System.Drawing.Size(900, 700);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
        ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);
        Font = new System.Drawing.Font("Segoe UI", 9f);
        Text = "DevPulse — AI spec review";
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;

        Controls.Add(_rtfSpec);
        Controls.Add(_lstHistory);
        Controls.Add(_lblMetadata);
        Controls.Add(_lblStatus);
        Controls.Add(toolbar);

        ResumeLayout(false);
    }
}
```

- [ ] **Step 2: Create code-behind**

`DevPulse.App/Forms/AiReviewForm.cs`:

```csharp
using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class AiReviewForm : Form
{
    private readonly IAiAttemptStore _attempts;
    private readonly AiPipelineService _pipeline;
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly IReadOnlyList<AiTemplate> _templates;
    private readonly WorkItem _workItem;

    private IReadOnlyList<AiAttempt> _history = [];
    private AiAttempt? _current;

    public AiReviewForm(
        IAiAttemptStore attempts,
        AiPipelineService pipeline,
        IReadOnlyList<IAiProvider> providers,
        IReadOnlyList<AiTemplate> templates,
        WorkItem workItem)
    {
        _attempts = attempts;
        _pipeline = pipeline;
        _providers = providers;
        _templates = templates;
        _workItem = workItem;
        InitializeComponent();
        Text = $"DevPulse — AI spec review — #{workItem.Id}";
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _history = await _attempts.GetAttemptsForWorkItemAsync(_workItem.Id);
            _lstHistory.Items.Clear();
            foreach (var a in _history)
                _lstHistory.Items.Add($"{a.CreatedAtUtc.ToLocalTime():MM/dd HH:mm}  [{a.Status}]  {a.ProviderId}");
            if (_lstHistory.Items.Count > 0)
                _lstHistory.SelectedIndex = 0;
        }
        catch (Exception ex) { Log.Error(ex, "AiReviewForm load failed"); }
    }

    private void LstHistory_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_lstHistory.SelectedIndex < 0 || _lstHistory.SelectedIndex >= _history.Count) return;
        _current = _history[_lstHistory.SelectedIndex];
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        if (_current == null) return;

        // Status banner
        (_lblStatus.Text, _lblStatus.BackColor) = _current.Status switch
        {
            AiAttemptStatus.Success => ($"✓ Generated via {_current.ProviderId} — {_current.DurationMs} ms — tokens in/out {_current.TokensIn}/{_current.TokensOut}",
                System.Drawing.Color.FromArgb(40, 100, 60)),
            AiAttemptStatus.ValidationFailed => ($"⚠ Validation failed — missing: {string.Join(", ", _current.MissingSections)}",
                System.Drawing.Color.FromArgb(140, 100, 40)),
            AiAttemptStatus.ProviderError => ($"✗ Provider error: {_current.ErrorMessage}",
                System.Drawing.Color.FromArgb(140, 60, 60)),
            AiAttemptStatus.Timeout => ($"✗ Timeout: {_current.ErrorMessage}",
                System.Drawing.Color.FromArgb(140, 60, 60)),
            _ => (_current.Status.ToString(), System.Drawing.Color.Gray)
        };

        // Spec content
        try
        {
            var markdown = File.Exists(_current.SpecFilePath)
                ? File.ReadAllText(_current.SpecFilePath)
                : "(spec file missing)";
            _rtfSpec.Rtf = MarkdownRenderer.ToRtf(markdown);
        }
        catch (Exception ex)
        {
            _rtfSpec.Text = $"Failed to load spec: {ex.Message}";
        }

        // Metadata
        _lblMetadata.Text =
            $"Template: {_current.TemplateId}\n" +
            $"Provider: {_current.ProviderId}\n" +
            $"Model: {_current.Model}\n" +
            $"Tokens in: {_current.TokensIn}\n" +
            $"Tokens out: {_current.TokensOut}\n" +
            $"Duration: {_current.DurationMs} ms\n" +
            $"Spec: {Path.GetFileName(_current.SpecFilePath)}\n" +
            $"Prompt: {Path.GetFileName(_current.PromptFilePath)}";
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        using var dlg = new AiGenerateDialog(_pipeline, _providers, _templates, _workItem);
        if (dlg.ShowDialog(this) == DialogResult.OK) await LoadAsync();
    }

    private void BtnCopy_Click(object? sender, EventArgs e)
    {
        if (_current == null || !File.Exists(_current.SpecFilePath)) return;
        Clipboard.SetText(File.ReadAllText(_current.SpecFilePath));
    }

    private void BtnOpenFolder_Click(object? sender, EventArgs e)
    {
        if (_current == null) return;
        var folder = Path.GetDirectoryName(_current.SpecFilePath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Forms/AiReviewForm.cs DevPulse.App/Forms/AiReviewForm.Designer.cs
git commit -m "feat(ai): AiReviewForm with status banner, history, rendered markdown, metadata"
```

---

### Task 20: `SettingsForm` AI tab (condensed)

**Files:**
- Modify: `DevPulse.App/Forms/SettingsForm.cs`
- Modify: `DevPulse.App/Forms/SettingsForm.Designer.cs`

This is the largest UI task. The tab adds fields for output root, provider config (Claude CLI path + detect, OpenRouter key + default model), and a minimal templates editor (listbox + per-template textarea).

- [ ] **Step 1: Add controls in Designer**

Append to `SettingsForm.Designer.cs` inside the existing designer class: new private fields for `_tabAi`, `_txtAiRoot`, `_btnAiRootBrowse`, `_txtClaudePath`, `_btnClaudeDetect`, `_chkClaudeEnabled`, `_txtOpenRouterKey`, `_txtOpenRouterModel`, `_chkOpenRouterEnabled`, `_lstAiTemplates`, `_txtTemplateBody`, `_txtTemplateHeaders`, `_btnTemplateNew`, `_btnTemplateDelete`. Add them to the new `_tabAi` tab page inside `InitializeComponent`, mirroring the patterns used by the existing Connection tab.

Full Designer code (append above the closing brace of `InitializeComponent`):

```csharp
// ── AI tab ─────────────────────────────────────────────────────────
_tabAi = new System.Windows.Forms.TabPage();
_tabAi.Text = "AI";
_tabAi.BackColor = System.Drawing.Color.FromArgb(30, 30, 46);
_tabAi.ForeColor = System.Drawing.Color.FromArgb(220, 220, 235);

var aiLayout = new System.Windows.Forms.TableLayoutPanel
{
    Dock = System.Windows.Forms.DockStyle.Fill,
    ColumnCount = 2,
    Padding = new System.Windows.Forms.Padding(12),
    BackColor = System.Drawing.Color.FromArgb(30, 30, 46)
};
aiLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 180));
aiLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));

void AddRow(string label, System.Windows.Forms.Control control)
{
    var lbl = new System.Windows.Forms.Label { Text = label, AutoSize = true, Padding = new System.Windows.Forms.Padding(0, 6, 0, 0),
                                                 ForeColor = System.Drawing.Color.FromArgb(180, 180, 200) };
    aiLayout.Controls.Add(lbl);
    aiLayout.Controls.Add(control);
}

_txtAiRoot = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill,
    BackColor = System.Drawing.Color.FromArgb(42, 42, 62), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("Output root:", _txtAiRoot);

_chkClaudeEnabled = new System.Windows.Forms.CheckBox { Text = "Claude Code CLI", AutoSize = true,
    ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("", _chkClaudeEnabled);

_txtClaudePath = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill,
    BackColor = System.Drawing.Color.FromArgb(42, 42, 62), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("Claude CLI path:", _txtClaudePath);

_btnClaudeDetect = new System.Windows.Forms.Button { Text = "Auto-detect", Width = 110,
    FlatStyle = System.Windows.Forms.FlatStyle.Flat,
    BackColor = System.Drawing.Color.FromArgb(50, 80, 120), ForeColor = System.Drawing.Color.White };
_btnClaudeDetect.Click += new System.EventHandler(BtnClaudeDetect_Click);
AddRow("", _btnClaudeDetect);

_chkOpenRouterEnabled = new System.Windows.Forms.CheckBox { Text = "OpenRouter (HTTP)", AutoSize = true,
    ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("", _chkOpenRouterEnabled);

_txtOpenRouterKey = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill,
    UseSystemPasswordChar = true,
    BackColor = System.Drawing.Color.FromArgb(42, 42, 62), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("OpenRouter API key:", _txtOpenRouterKey);

_txtOpenRouterModel = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill,
    BackColor = System.Drawing.Color.FromArgb(42, 42, 62), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("OpenRouter model:", _txtOpenRouterModel);

// Templates editor
_lstAiTemplates = new System.Windows.Forms.ListBox { Height = 80,
    BackColor = System.Drawing.Color.FromArgb(36, 36, 52), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
_lstAiTemplates.SelectedIndexChanged += new System.EventHandler(LstAiTemplates_SelectedIndexChanged);
_lstAiTemplates.Dock = System.Windows.Forms.DockStyle.Fill;
AddRow("Templates:", _lstAiTemplates);

_txtTemplateHeaders = new System.Windows.Forms.TextBox { Dock = System.Windows.Forms.DockStyle.Fill,
    BackColor = System.Drawing.Color.FromArgb(42, 42, 62), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("Required headers:", _txtTemplateHeaders);

_txtTemplateBody = new System.Windows.Forms.TextBox { Multiline = true, Dock = System.Windows.Forms.DockStyle.Fill,
    Height = 160, ScrollBars = System.Windows.Forms.ScrollBars.Vertical, Font = new System.Drawing.Font("Consolas", 8.5f),
    BackColor = System.Drawing.Color.FromArgb(42, 42, 62), ForeColor = System.Drawing.Color.FromArgb(220, 220, 235) };
AddRow("Template body:", _txtTemplateBody);

_tabAi.Controls.Add(aiLayout);
_tabs.TabPages.Add(_tabAi);
```

And declare the new private fields near the top of the partial class:

```csharp
private System.Windows.Forms.TabPage _tabAi;
private System.Windows.Forms.TextBox _txtAiRoot;
private System.Windows.Forms.CheckBox _chkClaudeEnabled;
private System.Windows.Forms.TextBox _txtClaudePath;
private System.Windows.Forms.Button _btnClaudeDetect;
private System.Windows.Forms.CheckBox _chkOpenRouterEnabled;
private System.Windows.Forms.TextBox _txtOpenRouterKey;
private System.Windows.Forms.TextBox _txtOpenRouterModel;
private System.Windows.Forms.ListBox _lstAiTemplates;
private System.Windows.Forms.TextBox _txtTemplateHeaders;
private System.Windows.Forms.TextBox _txtTemplateBody;
```

- [ ] **Step 2: Hook load/save in `SettingsForm.cs`**

Extend `LoadSettingsAsync` to populate AI fields, `SaveSettingsAsync` to persist them, and add the event handlers `BtnClaudeDetect_Click` and `LstAiTemplates_SelectedIndexChanged`.

```csharp
// In LoadSettingsAsync, after existing loads:
_txtAiRoot.Text = _appSettings.AiOutputRootPath;

var profiles = await _settings.GetAiProviderProfilesAsync();
var claude = profiles.FirstOrDefault(p => p.ProviderId == "claude-cli");
_chkClaudeEnabled.Checked = claude?.Enabled ?? false;
_txtClaudePath.Text = claude?.ExecutablePath ?? "";
var openRouter = profiles.FirstOrDefault(p => p.ProviderId == "openrouter");
_chkOpenRouterEnabled.Checked = openRouter?.Enabled ?? false;
_txtOpenRouterModel.Text = openRouter?.DefaultModel ?? "anthropic/claude-3.5-sonnet";
_txtOpenRouterKey.Text = DevPulse.Infrastructure.Security.SecretStore.TryLoadSecret("openrouter") is { IsOk: true, Value: var k } ? k : "";

_aiTemplates = (await new DevPulse.App.Services.SettingsAiTemplateStore(_settings).GetTemplatesAsync()).ToList();
_lstAiTemplates.Items.Clear();
foreach (var t in _aiTemplates) _lstAiTemplates.Items.Add(t.Name);
if (_lstAiTemplates.Items.Count > 0) _lstAiTemplates.SelectedIndex = 0;
```

Add a private field and handlers:

```csharp
private List<DevPulse.Core.Models.AiTemplate> _aiTemplates = [];
private int _selectedTemplateIdx = -1;

private void LstAiTemplates_SelectedIndexChanged(object? sender, EventArgs e)
{
    // Save edits to previous selection
    if (_selectedTemplateIdx >= 0 && _selectedTemplateIdx < _aiTemplates.Count)
    {
        _aiTemplates[_selectedTemplateIdx].RequiredHeaders =
            [.. _txtTemplateHeaders.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _aiTemplates[_selectedTemplateIdx].PromptBody = _txtTemplateBody.Text;
    }
    _selectedTemplateIdx = _lstAiTemplates.SelectedIndex;
    if (_selectedTemplateIdx < 0 || _selectedTemplateIdx >= _aiTemplates.Count) return;
    var t = _aiTemplates[_selectedTemplateIdx];
    _txtTemplateHeaders.Text = string.Join(", ", t.RequiredHeaders);
    _txtTemplateBody.Text = t.PromptBody;
}

private void BtnClaudeDetect_Click(object? sender, EventArgs e)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo("where", "claude")
        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit(3000);
        var first = p.StandardOutput.ReadToEnd().Split('\n').FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(first)) _txtClaudePath.Text = first;
        else MessageBox.Show("claude not found on PATH.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    catch (Exception ex) { MessageBox.Show($"Detect failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error); }
}
```

In `SaveSettingsAsync`, after existing saves, append:

```csharp
_appSettings.AiOutputRootPath = _txtAiRoot.Text.Trim();

// Capture current templates editor state
if (_selectedTemplateIdx >= 0 && _selectedTemplateIdx < _aiTemplates.Count)
{
    _aiTemplates[_selectedTemplateIdx].RequiredHeaders =
        [.. _txtTemplateHeaders.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    _aiTemplates[_selectedTemplateIdx].PromptBody = _txtTemplateBody.Text;
}

var aiProfiles = new List<DevPulse.Core.Models.AiProviderProfile>
{
    new() { ProviderId = "claude-cli", Enabled = _chkClaudeEnabled.Checked,
            ExecutablePath = _txtClaudePath.Text.Trim(), DefaultModel = "" },
    new() { ProviderId = "openrouter", Enabled = _chkOpenRouterEnabled.Checked,
            ExecutablePath = "", DefaultModel = _txtOpenRouterModel.Text.Trim() }
};

await _settings.SaveAiConfigAsync(aiProfiles, _aiTemplates);

// Save OpenRouter key via DPAPI (separate — if this fails, provider/template KV write is already committed)
if (!string.IsNullOrWhiteSpace(_txtOpenRouterKey.Text))
    DevPulse.Infrastructure.Security.SecretStore.SaveSecret("openrouter", _txtOpenRouterKey.Text);
```

(`SaveAppSettingsAsync` is called earlier in the existing flow — add a call after setting `AiOutputRootPath` if it isn't already triggered.)

- [ ] **Step 3: Build**

Run: `dotnet build --nologo`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Forms/SettingsForm.cs DevPulse.App/Forms/SettingsForm.Designer.cs
git commit -m "feat(ai): SettingsForm AI tab — output root, providers, templates editor"
```

---

### Task 21: BoardForm context menu + eligibility gating

**Files:**
- Modify: `DevPulse.App/UI/WorkItemCard.cs`
- Modify: `DevPulse.App/Forms/BoardForm.cs`

- [ ] **Step 1: Extend `WorkItemCard.BuildContextMenu` with three new items**

Replace the existing `BuildContextMenu` method:

```csharp
private ContextMenuStrip BuildContextMenu()
{
    var menu = new ContextMenuStrip();

    menu.Items.Add("Open in Azure DevOps", null, (_, _) =>
    {
        if (!string.IsNullOrEmpty(Item.WorkItemUrl))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Item.WorkItemUrl) { UseShellExecute = true });
    });

    menu.Items.Add(new ToolStripSeparator());

    var draftItem = new ToolStripMenuItem("Draft spec with AI…");
    draftItem.Click += (_, _) => OnDraftRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(draftItem);

    var viewItem = new ToolStripMenuItem("View AI drafts…");
    viewItem.Click += (_, _) => OnViewDraftsRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(viewItem);

    var folderItem = new ToolStripMenuItem("Open AI output folder");
    folderItem.Click += (_, _) => OnOpenFolderRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(folderItem);

    // Cache menu items for eligibility gating from BoardForm
    menu.Opening += (_, _) =>
    {
        draftItem.Enabled = Item.FirstSeenUtc.HasValue
            && (Item.State.Equals("New", StringComparison.OrdinalIgnoreCase)
                || Item.State.Equals("Proposed", StringComparison.OrdinalIgnoreCase));
        draftItem.ToolTipText = draftItem.Enabled
            ? "Draft an AI spec for this New/Proposed item"
            : "AI drafts are only available for first-seen New/Proposed items";
    };

    return menu;
}

public event EventHandler? OnDraftRequested;
public event EventHandler? OnViewDraftsRequested;
public event EventHandler? OnOpenFolderRequested;
```

- [ ] **Step 2: Wire events in `BoardForm.cs`**

When creating a `WorkItemCard` (inside the method that builds cards — likely in `BoardColumnPanel` or `BoardForm` directly), attach handlers that look up services via a new `AiPipelineService` field injected from `TrayApplicationContext`.

Add a field to `BoardForm`:

```csharp
private AiPipelineService? _aiPipeline;
private IAiProvider[]? _aiProviders;
private IReadOnlyList<AiTemplate>? _aiTemplates;

public void AttachAi(AiPipelineService pipeline, IEnumerable<IAiProvider> providers, IReadOnlyList<AiTemplate> templates)
{
    _aiPipeline = pipeline;
    _aiProviders = providers.ToArray();
    _aiTemplates = templates;
}
```

Wire the events when instantiating each `WorkItemCard` (inside the rendering loop — locate where `new WorkItemCard(item)` is called and add):

```csharp
card.OnDraftRequested += (_, _) =>
{
    if (_aiPipeline == null || _aiProviders == null || _aiTemplates == null) return;
    using var dlg = new AiGenerateDialog(_aiPipeline, _aiProviders, _aiTemplates, card.Item);
    if (dlg.ShowDialog(this) == DialogResult.OK)
    {
        var review = new AiReviewForm(_store as IAiAttemptStore ?? throw new InvalidOperationException("store is not IAiAttemptStore"),
            _aiPipeline, _aiProviders, _aiTemplates, card.Item);
        review.Show(this);
    }
};
card.OnViewDraftsRequested += (_, _) =>
{
    if (_aiPipeline == null || _aiProviders == null || _aiTemplates == null) return;
    var review = new AiReviewForm((IAiAttemptStore)_store, _aiPipeline, _aiProviders, _aiTemplates, card.Item);
    review.Show(this);
};
card.OnOpenFolderRequested += (_, _) =>
{
    var root = _appSettings.AiOutputRootPath;
    var slug = DevPulse.Infrastructure.Ai.FilesystemSpecWriter.Slugify(_appSettings.Project);
    var folder = Path.Combine(root, slug, card.Item.Id.ToString());
    if (Directory.Exists(folder))
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
};
```

Note: `FilesystemSpecWriter.Slugify` is `internal` — change its modifier to `public static` OR expose a public `SlugifyHelper` in `DevPulse.Core.Services` that both the writer and BoardForm use. Do the latter for cleaner boundaries:

```csharp
// DevPulse.Core/Services/Slugify.cs
namespace DevPulse.Core.Services;

public static class Slugify
{
    public static string Project(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
```

Update `FilesystemSpecWriter` to call `Slugify.Project(...)` instead of its private helper.

- [ ] **Step 3: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/UI/WorkItemCard.cs DevPulse.App/Forms/BoardForm.cs DevPulse.Core/Services/Slugify.cs DevPulse.Infrastructure/Ai/FilesystemSpecWriter.cs
git commit -m "feat(ai): BoardForm context menu — Draft/View/Open folder with eligibility gating"
```

---

### Task 22: TrayApplicationContext wire-up

**Files:**
- Modify: `DevPulse.App/TrayApplicationContext.cs`

- [ ] **Step 1: Construct providers and pipeline during `InitializeAsync`**

After the `_prPoller` / `_wiPoller` construction section, add:

```csharp
// AI providers
var aiProfiles = await _settings.GetAiProviderProfilesAsync();
var claudeProfile = aiProfiles.FirstOrDefault(p => p.ProviderId == "claude-cli");
var openRouterProfile = aiProfiles.FirstOrDefault(p => p.ProviderId == "openrouter");

var aiProviders = new List<IAiProvider>();
if (claudeProfile?.Enabled == true && !string.IsNullOrEmpty(claudeProfile.ExecutablePath))
    aiProviders.Add(new DevPulse.Infrastructure.Ai.ClaudeCliProvider(claudeProfile.ExecutablePath));
if (openRouterProfile?.Enabled == true)
{
    var aiHttp = CreateHttpClient(appSettings.OrganizationUrl, ""); // reuse HttpClient shape
    aiProviders.Add(new DevPulse.Infrastructure.Ai.OpenRouterProvider(
        new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) },
        () => DevPulse.Infrastructure.Security.SecretStore.TryLoadSecret("openrouter") is { IsOk: true, Value: var k } ? k : null));
}

var aiTemplates = await new DevPulse.App.Services.SettingsAiTemplateStore(_settings).GetTemplatesAsync();
var writer = new DevPulse.Infrastructure.Ai.FilesystemSpecWriter();
_aiPipeline = new DevPulse.App.Services.AiPipelineService(
    aiProviders, new DevPulse.App.Services.SettingsAiTemplateStore(_settings), writer, _store,
    appSettings.AiOutputRootPath, appSettings.Project,
    async id => (await _store.GetWorkItemsAsync()).FirstOrDefault(w => w.Id == id));

_aiProviders = aiProviders;
_aiTemplates = aiTemplates;
```

Add fields:

```csharp
private DevPulse.App.Services.AiPipelineService? _aiPipeline;
private List<DevPulse.Core.Interfaces.IAiProvider>? _aiProviders;
private IReadOnlyList<DevPulse.Core.Models.AiTemplate>? _aiTemplates;
```

In `ShowBoard()`, after creating `_boardForm`, call:

```csharp
if (_aiPipeline != null && _aiProviders != null && _aiTemplates != null)
    _boardForm.AttachAi(_aiPipeline, _aiProviders, _aiTemplates);
```

- [ ] **Step 2: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 3: Full test suite**

Run: `dotnet test --nologo --no-build`
Expected: all existing and new tests pass.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/TrayApplicationContext.cs
git commit -m "feat(ai): wire providers, templates, and AiPipelineService into tray context"
```

---

### Task 23: Manual smoke test checklist

Not code. Save to `docs/superpowers/plans/ai-pipeline-smoke-test.md` and run after the wire-up task lands.

- [ ] **Step 1: Create smoke-test checklist**

```markdown
# AI Pipeline MVP Smoke Test

Run after Task 22 lands.

## Pre-flight
- [ ] DevPulse builds: `dotnet build --nologo`
- [ ] All tests pass: `dotnet test --nologo --no-build`
- [ ] Claude CLI installed and on PATH (`where claude` returns a path)
- [ ] OpenRouter account with API key available

## Settings flow
- [ ] Open Settings → AI tab loads with default templates listed
- [ ] Auto-detect Claude path populates the textbox from `where claude`
- [ ] Paste OpenRouter API key, set default model `anthropic/claude-3.5-sonnet`
- [ ] Enable both providers, Save — no error dialog

## Generation flow (Claude CLI)
- [ ] Wait for a work item poll to complete; confirm at least one New item on the board
- [ ] Right-click a New item → "Draft spec with AI…" → dialog opens with the item title
- [ ] Template dropdown pre-selects by work item type (Bug → Bug template)
- [ ] Provider dropdown shows `[LOCAL] Claude Code CLI`
- [ ] Click Generate → button reads "Generating…"; dialog closes on completion
- [ ] Review form opens showing rendered markdown spec
- [ ] Status banner is green with duration/tokens
- [ ] Spec + prompt files exist in `C:\devops\<project>\<id>\`
- [ ] `meta.json` contains an attempt record with `status: "success"`

## Generation flow (OpenRouter)
- [ ] Right-click same or different work item → pick `[CLOUD] OpenRouter`
- [ ] Warning line appears: "This will send the work item title…"
- [ ] Generate → success banner
- [ ] New history entry in review form (2 attempts now visible)
- [ ] Selecting older entry switches rendered spec

## Error paths
- [ ] Rename `claude.exe` temporarily → re-run Draft → Claude provider shows disabled in dropdown
- [ ] Restore `claude.exe`, set OpenRouter API key to empty in Settings → Draft with OpenRouter → auth error banner in review
- [ ] Set a template's required headers to include a fake header "Zebra" → Generate → validation_failed banner; missing header listed

## Eligibility gating
- [ ] Move a work item to "Active" → right-click → "Draft spec with AI…" is disabled; tooltip explains
- [ ] First-seen item that's been re-polled: `first_seen_utc` unchanged (check `ai_attempts` table or manual SQL)

## Audit trail
- [ ] `ai_attempts` table has one row per attempt (success + failed)
- [ ] `meta.json` mirrors the DB row count per work item folder
- [ ] Deleting `meta.json` and reopening review form still shows history (DB is source of truth)
```

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/plans/ai-pipeline-smoke-test.md
git commit -m "docs: manual smoke-test checklist for AI pipeline MVP"
```

---

## Self-Review (Part 2)

1. **Spec coverage:** Settings infrastructure (§2 kv_settings, §3 SettingsAiTemplateStore, §7 SettingsForm) ✓. Pipeline orchestrator (§3 AiPipelineService, §4 flow) ✓. Dialog + Review form + SettingsForm tab + BoardForm context menu (§7) ✓. Templates shipped with defaults (§3) ✓. Atomic save (§6) ✓. Wire-up in TrayApplicationContext ✓. Smoke test covers the manual verification items in §8.
2. **Placeholder scan:** No TBD/TODOs. Every step has concrete code or commands. The Designer code for `AiGenerateDialog` and `AiReviewForm` is complete; `SettingsForm` AI tab is additive with explicit code for new controls.
3. **Type consistency:** `AiPipelineService` ctor signature (providers, templates, writer, attempts, root, slug, loadWorkItem) matches across Task 16 (test and impl), Task 21 (wire), Task 22 (construction). `AiGenerateDialog` ctor matches across Task 18 definition and Task 19/21 consumers. `IAiAttemptStore` cast in Task 21 assumes `SqliteStateStore : IAiAttemptStore` — which will hold because the interface additions in Part 1 Task 7 added those methods directly onto `IStateStore` (which `SqliteStateStore` already implements). Compatible without an explicit second interface.
4. **Scope check:** Part 2 is UI + wire-up only; Part 1 covered all logic. Can ship Part 1 independently (logic works, no UI path), then Part 2 surfaces it.

No issues found. Plan is complete.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-21-ai-action-pipeline.md` (Part 1, Tasks 1–12) and `docs/superpowers/plans/2026-04-21-ai-action-pipeline-part2.md` (Part 2, Tasks 13–23).

Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
