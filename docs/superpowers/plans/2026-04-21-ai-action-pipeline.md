# AI Action Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a manual-trigger AI spec-drafting pipeline to DevPulse: right-click a New work item → pick provider + template → save versioned markdown spec to `C:\devops\<project>\<ticketId>\` with full DB + filesystem audit trail.

**Architecture:** Additive service layer (Approach 1 from spec). New interfaces/models/services in existing Core/Infrastructure/App projects. Schema v2 migration adds `ai_attempts` table + `work_items.first_seen_utc`. Two providers at MVP: Claude Code CLI (subprocess) and OpenRouter (HTTP). Manual trigger only, no background jobs.

**Tech Stack:** .NET 8, WinForms, SQLite (Microsoft.Data.Sqlite), xUnit + FluentAssertions, System.Diagnostics.Process for CLI, existing HttpClient patterns for HTTP, DPAPI via SecretStore for API key.

**Spec source of truth:** `docs/superpowers/specs/2026-04-21-ai-action-pipeline-design.md`

**Test commands:**
- Build: `dotnet build --nologo`
- Test: `dotnet test --nologo --no-build`
- Single test: `dotnet test --nologo --no-build --filter "FullyQualifiedName~ClassName.MethodName"`

---

## Phase 1 — Core enums, models, interfaces (scaffolding)

### Task 1: Add Core enums

**Files:**
- Create: `DevPulse.Core/Enums/AiProviderKind.cs`
- Create: `DevPulse.Core/Enums/AiDataPolicy.cs`
- Create: `DevPulse.Core/Enums/AiAttemptStatus.cs`

- [ ] **Step 1: Create `AiProviderKind.cs`**

```csharp
namespace DevPulse.Core.Enums;

public enum AiProviderKind { Cli, Http }
```

- [ ] **Step 2: Create `AiDataPolicy.cs`**

```csharp
namespace DevPulse.Core.Enums;

public enum AiDataPolicy { Local, Cloud }
```

- [ ] **Step 3: Create `AiAttemptStatus.cs`**

```csharp
namespace DevPulse.Core.Enums;

public enum AiAttemptStatus { Success, ValidationFailed, ProviderError, Timeout }
```

- [ ] **Step 4: Build**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Enums/AiProviderKind.cs DevPulse.Core/Enums/AiDataPolicy.cs DevPulse.Core/Enums/AiAttemptStatus.cs
git commit -m "feat(ai): add core enums for provider kind, data policy, attempt status"
```

---

### Task 2: Add Core AI data models (records and POCOs)

**Files:**
- Create: `DevPulse.Core/Models/AiTemplate.cs`
- Create: `DevPulse.Core/Models/AiProviderProfile.cs`
- Create: `DevPulse.Core/Models/AiAttempt.cs`
- Create: `DevPulse.Core/Models/AiFilePaths.cs`
- Create: `DevPulse.Core/Models/AiGenerateRequest.cs`
- Create: `DevPulse.Core/Models/AiGenerateResult.cs`
- Create: `DevPulse.Core/Models/AiHealthResult.cs`
- Create: `DevPulse.Core/Models/AiValidationResult.cs`

- [ ] **Step 1: Create `AiTemplate.cs`**

```csharp
namespace DevPulse.Core.Models;

public sealed class AiTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> AppliesTo { get; set; } = [];
    public List<string> RequiredHeaders { get; set; } = [];
    public string PromptBody { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create `AiProviderProfile.cs`**

```csharp
namespace DevPulse.Core.Models;

public sealed class AiProviderProfile
{
    public string ProviderId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string DefaultModel { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 90;
}
```

- [ ] **Step 3: Create `AiAttempt.cs`**

```csharp
using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class AiAttempt
{
    public string Id { get; set; } = string.Empty;
    public int WorkItemId { get; set; }
    public string Project { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public AiAttemptStatus Status { get; set; }
    public bool ValidationPassed { get; set; }
    public List<string> MissingSections { get; set; } = [];
    public string SpecFilePath { get; set; } = string.Empty;
    public string PromptFilePath { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 4: Create `AiFilePaths.cs`**

```csharp
namespace DevPulse.Core.Models;

public sealed record AiFilePaths(string SpecPath, string PromptPath, string MetaPath);
```

- [ ] **Step 5: Create `AiGenerateRequest.cs`, `AiGenerateResult.cs`, `AiHealthResult.cs`**

```csharp
// AiGenerateRequest.cs
namespace DevPulse.Core.Models;
public sealed record AiGenerateRequest(string Prompt, string Model, TimeSpan Timeout);
```

```csharp
// AiGenerateResult.cs
namespace DevPulse.Core.Models;
public sealed record AiGenerateResult(
    string Markdown,
    string ModelUsed,
    int TokensIn,
    int TokensOut,
    TimeSpan Duration,
    string? ErrorMessage);
```

```csharp
// AiHealthResult.cs
namespace DevPulse.Core.Models;
public sealed record AiHealthResult(bool Ok, string? ErrorMessage);
```

- [ ] **Step 6: Create `AiValidationResult.cs`**

```csharp
namespace DevPulse.Core.Models;

public sealed class AiValidationResult
{
    public bool IsValid { get; set; }
    public List<string> MissingHeaders { get; set; } = [];
    public List<string> EmptySections { get; set; } = [];
}
```

- [ ] **Step 7: Build**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add DevPulse.Core/Models/Ai*.cs
git commit -m "feat(ai): add core models for templates, providers, attempts, validation"
```

---

### Task 3: Add Core interfaces

**Files:**
- Create: `DevPulse.Core/Interfaces/IAiProvider.cs`
- Create: `DevPulse.Core/Interfaces/IAiTemplateStore.cs`
- Create: `DevPulse.Core/Interfaces/IAiSpecWriter.cs`
- Create: `DevPulse.Core/Interfaces/IAiAttemptStore.cs`

- [ ] **Step 1: Create `IAiProvider.cs`**

```csharp
using DevPulse.Core.Enums;
using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiProvider
{
    string Id { get; }
    string DisplayName { get; }
    AiProviderKind Kind { get; }
    AiDataPolicy DataPolicy { get; }
    Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default);
    Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `IAiTemplateStore.cs`**

```csharp
using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiTemplateStore
{
    Task<List<AiTemplate>> GetTemplatesAsync(CancellationToken ct = default);
    Task SaveTemplatesAsync(List<AiTemplate> templates, CancellationToken ct = default);
    Task<AiTemplate?> GetDefaultTemplateForAsync(string workItemType, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create `IAiSpecWriter.cs`**

```csharp
using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiSpecWriter
{
    Task<AiFilePaths> WriteAsync(
        string outputRoot,
        string projectSlug,
        int workItemId,
        DateTimeOffset timestampUtc,
        string specMarkdown,
        string promptMarkdown,
        IReadOnlyList<AiAttempt> attemptHistory,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `IAiAttemptStore.cs`**

```csharp
using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiAttemptStore
{
    Task RecordAttemptAsync(AiAttempt attempt, CancellationToken ct = default);
    Task<IReadOnlyList<AiAttempt>> GetAttemptsForWorkItemAsync(int workItemId, CancellationToken ct = default);
}
```

- [ ] **Step 5: Build**

Run: `dotnet build --nologo`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add DevPulse.Core/Interfaces/IAi*.cs
git commit -m "feat(ai): add core interfaces for provider, template store, writer, attempt store"
```

---

## Phase 2 — Pure Core services (TDD)

### Task 4: AiOutputValidator — TDD

**Files:**
- Create: `DevPulse.Core/Services/AiOutputValidator.cs`
- Create: `DevPulse.Tests/AiOutputValidatorTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DevPulse.Tests/AiOutputValidatorTests.cs`:

```csharp
using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

public class AiOutputValidatorTests
{
    private readonly AiOutputValidator _sut = new();
    private static readonly List<string> RequiredHeaders =
        ["Context summary", "Functional requirements", "Acceptance criteria",
         "Edge cases", "Test plan", "Risks and dependencies"];

    [Fact]
    public void Validate_AllHeadersPresentWithContent_IsValid()
    {
        var md = """
            ## Context summary
            Some context.
            ## Functional requirements
            A requirement.
            ## Acceptance criteria
            Given x, when y, then z.
            ## Edge cases
            Edge case 1.
            ## Test plan
            A test.
            ## Risks and dependencies
            None known.
            """;

        var result = _sut.Validate(md, RequiredHeaders);

        result.IsValid.Should().BeTrue();
        result.MissingHeaders.Should().BeEmpty();
        result.EmptySections.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingHeader_IsInvalid()
    {
        var md = "## Context summary\nSome context.\n## Test plan\nA test.";

        var result = _sut.Validate(md, RequiredHeaders);

        result.IsValid.Should().BeFalse();
        result.MissingHeaders.Should().Contain("Functional requirements");
        result.MissingHeaders.Should().Contain("Acceptance criteria");
    }

    [Fact]
    public void Validate_HeaderPresentButEmptyBody_IsInvalid()
    {
        var md = """
            ## Context summary

            ## Functional requirements
            Has content.
            """;

        var result = _sut.Validate(md, ["Context summary", "Functional requirements"]);

        result.IsValid.Should().BeFalse();
        result.EmptySections.Should().Contain("Context summary");
        result.EmptySections.Should().NotContain("Functional requirements");
    }

    [Fact]
    public void Validate_HeaderComparisonIsCaseInsensitive()
    {
        var md = "## context SUMMARY\nSome content.";

        var result = _sut.Validate(md, ["Context summary"]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NestedH3DoesNotSplitSection()
    {
        var md = """
            ## Context summary
            Intro.
            ### Subsection
            Nested content.
            ## Test plan
            A test.
            """;

        var result = _sut.Validate(md, ["Context summary", "Test plan"]);

        result.IsValid.Should().BeTrue();
        result.EmptySections.Should().BeEmpty();
    }

    [Fact]
    public void Validate_UnknownExtraHeadersAreTolerated()
    {
        var md = """
            ## Context summary
            content
            ## Bonus section
            extra
            """;

        var result = _sut.Validate(md, ["Context summary"]);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhitespaceOnlyBody_TreatedAsEmpty()
    {
        var md = "## Context summary\n   \n\t\n## Test plan\ncontent";

        var result = _sut.Validate(md, ["Context summary", "Test plan"]);

        result.EmptySections.Should().Contain("Context summary");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AiOutputValidatorTests"`
Expected: build fails — `AiOutputValidator` not defined.

- [ ] **Step 3: Implement `AiOutputValidator`**

Create `DevPulse.Core/Services/AiOutputValidator.cs`:

```csharp
using System.Text.RegularExpressions;
using DevPulse.Core.Models;

namespace DevPulse.Core.Services;

public sealed class AiOutputValidator
{
    private static readonly Regex H2Regex = new(@"^##\s+(.+?)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    public AiValidationResult Validate(string markdown, IReadOnlyList<string> requiredHeaders)
    {
        var result = new AiValidationResult { IsValid = true };
        if (string.IsNullOrEmpty(markdown))
        {
            result.IsValid = false;
            result.MissingHeaders = [.. requiredHeaders];
            return result;
        }

        var matches = H2Regex.Matches(markdown);
        var presentHeaders = matches
            .Select(m => (Name: m.Groups[1].Value.Trim(), Index: m.Index, EndIndex: m.Index + m.Length))
            .ToList();

        foreach (var required in requiredHeaders)
        {
            var match = presentHeaders.FirstOrDefault(p =>
                p.Name.Equals(required, StringComparison.OrdinalIgnoreCase));
            if (match == default)
            {
                result.MissingHeaders.Add(required);
                result.IsValid = false;
                continue;
            }

            // Find the body between this header end and the next H2 (or EOF)
            var thisIdx = presentHeaders.FindIndex(p => p.Index == match.Index);
            var bodyStart = match.EndIndex;
            var bodyEnd = thisIdx + 1 < presentHeaders.Count
                ? presentHeaders[thisIdx + 1].Index
                : markdown.Length;
            var body = markdown[bodyStart..bodyEnd];
            if (string.IsNullOrWhiteSpace(body))
            {
                result.EmptySections.Add(required);
                result.IsValid = false;
            }
        }

        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AiOutputValidatorTests"`
Expected: `Passed! - Failed: 0, Passed: 7`

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Services/AiOutputValidator.cs DevPulse.Tests/AiOutputValidatorTests.cs
git commit -m "feat(ai): add AiOutputValidator with H2 section-structure validation"
```

---

### Task 5: AiTemplateRenderer — TDD

**Files:**
- Create: `DevPulse.Core/Services/AiTemplateRenderer.cs`
- Create: `DevPulse.Tests/AiTemplateRendererTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using FluentAssertions;

namespace DevPulse.Tests;

public class AiTemplateRendererTests
{
    private readonly AiTemplateRenderer _sut = new();

    private static WorkItem MakeWorkItem() => new()
    {
        Id = 42,
        Title = "Add export button",
        State = "New",
        AreaPath = @"MyProject\Feature",
        IterationPath = @"MyProject\Sprint 1",
    };

    [Fact]
    public void Render_SubstitutesAllowedTokens()
    {
        var wi = MakeWorkItem();
        const string body = "Title: {title}\nState: {state}\nArea: {areaPath}";

        var result = _sut.Render(body, wi, description: "Button exports to CSV", acceptanceCriteria: "G/W/T");

        result.Should().Contain("Title: Add export button");
        result.Should().Contain("State: New");
        result.Should().Contain(@"Area: MyProject\Feature");
    }

    [Fact]
    public void Render_UnknownTokensLeftLiteral()
    {
        var result = _sut.Render("Hello {unknown}!", MakeWorkItem(), description: "", acceptanceCriteria: "");
        result.Should().Contain("{unknown}");
    }

    [Fact]
    public void Render_MissingFieldsSubstitutedAsEmpty()
    {
        var wi = new WorkItem { Id = 1 }; // Title, State, etc. all empty strings
        var result = _sut.Render("T:{title}|S:{state}|D:{description}", wi, description: "", acceptanceCriteria: "");
        result.Should().Be("T:|S:|D:");
    }

    [Fact]
    public void Render_SubstitutesDescriptionAndAcceptance()
    {
        var result = _sut.Render(
            "{description}\n---\n{acceptanceCriteria}",
            MakeWorkItem(),
            description: "Users need to export",
            acceptanceCriteria: "Given X, When Y, Then Z");

        result.Should().Contain("Users need to export");
        result.Should().Contain("Given X, When Y, Then Z");
    }

    [Theory]
    [InlineData("{title}")]
    [InlineData("{description}")]
    [InlineData("{areaPath}")]
    [InlineData("{iterationPath}")]
    [InlineData("{type}")]
    [InlineData("{state}")]
    [InlineData("{acceptanceCriteria}")]
    public void Render_AllowListedTokensSubstitute(string token)
    {
        var result = _sut.Render(token, MakeWorkItem(), description: "d", acceptanceCriteria: "ac");
        result.Should().NotContain("{");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AiTemplateRendererTests"`
Expected: build fails — `AiTemplateRenderer` not defined.

- [ ] **Step 3: Implement `AiTemplateRenderer`**

```csharp
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.Core.Services;

public sealed class AiTemplateRenderer
{
    private static readonly HashSet<string> AllowedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "description", "areaPath", "iterationPath", "type", "state", "acceptanceCriteria"
    };

    public string Render(string templateBody, WorkItem workItem, string description, string acceptanceCriteria)
    {
        if (string.IsNullOrEmpty(templateBody)) return string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = workItem.Title ?? string.Empty,
            ["description"] = description ?? string.Empty,
            ["areaPath"] = workItem.AreaPath ?? string.Empty,
            ["iterationPath"] = workItem.IterationPath ?? string.Empty,
            ["type"] = workItem.Type.ToString(),
            ["state"] = workItem.State ?? string.Empty,
            ["acceptanceCriteria"] = acceptanceCriteria ?? string.Empty,
        };

        return System.Text.RegularExpressions.Regex.Replace(
            templateBody,
            @"\{([a-zA-Z]+)\}",
            match =>
            {
                var tokenName = match.Groups[1].Value;
                if (values.TryGetValue(tokenName, out var value))
                    return value;
                if (!AllowedTokens.Contains(tokenName))
                    Log.Warning("AiTemplateRenderer: unknown token '{Token}' left literal", tokenName);
                return match.Value; // leave literal
            });
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AiTemplateRendererTests"`
Expected: `Passed! - Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Services/AiTemplateRenderer.cs DevPulse.Tests/AiTemplateRendererTests.cs
git commit -m "feat(ai): add AiTemplateRenderer with allow-listed token substitution"
```

---

## Phase 3 — Schema migration

### Task 6: DbSchema v2 — `ai_attempts` table + `work_items.first_seen_utc` column

**Files:**
- Modify: `DevPulse.Infrastructure/Persistence/DbSchema.cs`
- Create: `DevPulse.Tests/DbSchemaV2MigrationTests.cs`

- [ ] **Step 1: Write the failing migration test**

Create `DevPulse.Tests/DbSchemaV2MigrationTests.cs`:

```csharp
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DevPulse.Tests;

public class DbSchemaV2MigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EnsureCreatedAsync_CreatesAiAttemptsTable()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ai_attempts'";
        var name = (string?)await cmd.ExecuteScalarAsync();
        name.Should().Be("ai_attempts");
    }

    [Fact]
    public async Task EnsureCreatedAsync_AddsFirstSeenUtcToWorkItems()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(work_items)";
        await using var reader = await cmd.ExecuteReaderAsync();
        var cols = new List<string>();
        while (await reader.ReadAsync()) cols.Add(reader.GetString(1));
        cols.Should().Contain("first_seen_utc");
    }

    [Fact]
    public async Task EnsureCreatedAsync_SetsSchemaVersionTo2()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM db_meta WHERE key='schema_version'";
        var v = (string?)await cmd.ExecuteScalarAsync();
        v.Should().Be("2");
    }

    [Fact]
    public async Task EnsureCreatedAsync_IsIdempotent()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);
        // second call must not throw
        await DbSchema.EnsureCreatedAsync(conn);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~DbSchemaV2MigrationTests"`
Expected: failures — `ai_attempts` not found, `first_seen_utc` not found, schema_version is "1".

- [ ] **Step 3: Bump `CurrentSchemaVersion` to 2 and add migration SQL**

In `DevPulse.Infrastructure/Persistence/DbSchema.cs`, change the const:

```csharp
public const int CurrentSchemaVersion = 2;
```

Add the `ai_attempts` table to the main `CREATE TABLE IF NOT EXISTS` block (place it after `kv_settings`):

```sql
CREATE TABLE IF NOT EXISTS ai_attempts (
    id TEXT PRIMARY KEY,
    work_item_id INTEGER NOT NULL,
    project TEXT NOT NULL DEFAULT '',
    template_id TEXT NOT NULL DEFAULT '',
    provider_id TEXT NOT NULL DEFAULT '',
    model TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT '',
    validation_passed INTEGER NOT NULL DEFAULT 0,
    missing_sections TEXT NOT NULL DEFAULT '',
    spec_file_path TEXT NOT NULL DEFAULT '',
    prompt_file_path TEXT NOT NULL DEFAULT '',
    duration_ms INTEGER NOT NULL DEFAULT 0,
    tokens_in INTEGER NOT NULL DEFAULT 0,
    tokens_out INTEGER NOT NULL DEFAULT 0,
    created_at_utc TEXT NOT NULL DEFAULT '',
    error_message TEXT
);
CREATE INDEX IF NOT EXISTS idx_ai_attempts_wi ON ai_attempts(work_item_id, created_at_utc DESC);
```

Extend the existing `ALTER TABLE` idempotent block to also add `work_items.first_seen_utc`:

```csharp
foreach (var alter in new[]
{
    "ALTER TABLE mute_entries ADD COLUMN pr_id INTEGER",
    "ALTER TABLE mute_entries ADD COLUMN author_key TEXT NOT NULL DEFAULT ''",
    "ALTER TABLE work_items ADD COLUMN first_seen_utc TEXT"
})
{
    try
    {
        await using var m = conn.CreateCommand();
        m.CommandText = alter;
        await m.ExecuteNonQueryAsync();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 1 &&
        ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase)) { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~DbSchemaV2MigrationTests"`
Expected: `Passed! - Failed: 0, Passed: 4`

Run full test suite to ensure no regression: `dotnet test --nologo --no-build`
Expected: all existing 28 + new tests pass.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/Persistence/DbSchema.cs DevPulse.Tests/DbSchemaV2MigrationTests.cs
git commit -m "feat(ai): schema v2 — ai_attempts table + work_items.first_seen_utc column"
```

---

### Task 7: SqliteStateStore — implement IAiAttemptStore + first_seen_utc tracking

**Files:**
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs`
- Modify: `DevPulse.Core/Interfaces/IStateStore.cs`
- Create: `DevPulse.Tests/SqliteAiAttemptStoreTests.cs`

- [ ] **Step 1: Add methods to IStateStore**

In `DevPulse.Core/Interfaces/IStateStore.cs`, add at the bottom before the closing brace:

```csharp
// AI attempts
Task RecordAiAttemptAsync(AiAttempt attempt, CancellationToken ct = default);
Task<IReadOnlyList<AiAttempt>> GetAiAttemptsForWorkItemAsync(int workItemId, CancellationToken ct = default);
```

Ensure the `using DevPulse.Core.Models;` at the top covers `AiAttempt`.

- [ ] **Step 2: Write the failing test**

Create `DevPulse.Tests/SqliteAiAttemptStoreTests.cs`:

```csharp
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SqliteAiAttemptStoreTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-ai-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task RecordAndRetrieve_RoundTripsAllFields()
    {
        var attempt = new AiAttempt
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = 42,
            Project = "MyProject",
            TemplateId = "bug-default",
            ProviderId = "claude-cli",
            Model = "claude-3-5-sonnet",
            Status = AiAttemptStatus.Success,
            ValidationPassed = true,
            MissingSections = [],
            SpecFilePath = @"C:\devops\MyProject\42\spec-ts.md",
            PromptFilePath = @"C:\devops\MyProject\42\prompt-ts.md",
            DurationMs = 1234,
            TokensIn = 500,
            TokensOut = 800,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ErrorMessage = null
        };

        await _store.RecordAiAttemptAsync(attempt);

        var list = await _store.GetAiAttemptsForWorkItemAsync(42);
        list.Should().HaveCount(1);
        var loaded = list[0];
        loaded.Id.Should().Be(attempt.Id);
        loaded.Status.Should().Be(AiAttemptStatus.Success);
        loaded.ValidationPassed.Should().BeTrue();
        loaded.SpecFilePath.Should().Be(attempt.SpecFilePath);
        loaded.DurationMs.Should().Be(1234);
    }

    [Fact]
    public async Task GetAttempts_OrdersNewestFirst()
    {
        var older = new AiAttempt
        {
            Id = "a1", WorkItemId = 7, Status = AiAttemptStatus.Success,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var newer = new AiAttempt
        {
            Id = "a2", WorkItemId = 7, Status = AiAttemptStatus.ProviderError,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        await _store.RecordAiAttemptAsync(older);
        await _store.RecordAiAttemptAsync(newer);

        var list = await _store.GetAiAttemptsForWorkItemAsync(7);
        list[0].Id.Should().Be("a2");
        list[1].Id.Should().Be("a1");
    }

    [Fact]
    public async Task Record_ValidationFailed_PersistsMissingSectionsCommaSeparated()
    {
        var attempt = new AiAttempt
        {
            Id = Guid.NewGuid().ToString(),
            WorkItemId = 1,
            Status = AiAttemptStatus.ValidationFailed,
            ValidationPassed = false,
            MissingSections = ["Edge cases", "Test plan"],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await _store.RecordAiAttemptAsync(attempt);
        var loaded = (await _store.GetAiAttemptsForWorkItemAsync(1))[0];
        loaded.MissingSections.Should().BeEquivalentTo("Edge cases", "Test plan");
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~SqliteAiAttemptStoreTests"`
Expected: build fails — methods not defined.

- [ ] **Step 4: Implement in SqliteStateStore**

Add a small enum ↔ string converter helper near the other `ReadEvent` helpers:

```csharp
private static readonly Dictionary<AiAttemptStatus, string> AiStatusToString = new()
{
    [AiAttemptStatus.Success] = "success",
    [AiAttemptStatus.ValidationFailed] = "validation_failed",
    [AiAttemptStatus.ProviderError] = "provider_error",
    [AiAttemptStatus.Timeout] = "timeout",
};

private static readonly Dictionary<string, AiAttemptStatus> StringToAiStatus = new(StringComparer.OrdinalIgnoreCase)
{
    ["success"] = AiAttemptStatus.Success,
    ["validation_failed"] = AiAttemptStatus.ValidationFailed,
    ["provider_error"] = AiAttemptStatus.ProviderError,
    ["timeout"] = AiAttemptStatus.Timeout,
};

private static AiAttemptStatus ParseAiStatus(string s)
{
    if (StringToAiStatus.TryGetValue(s, out var v)) return v;
    Log.Warning("SqliteStateStore: unknown ai_attempts.status '{Status}' — defaulting to ProviderError", s);
    return AiAttemptStatus.ProviderError;
}
```

Add the two methods near the other data-access methods:

```csharp
public async Task RecordAiAttemptAsync(AiAttempt attempt, CancellationToken ct = default)
{
    await _lock.WaitAsync(ct);
    try
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ai_attempts (
                id, work_item_id, project, template_id, provider_id, model,
                status, validation_passed, missing_sections,
                spec_file_path, prompt_file_path,
                duration_ms, tokens_in, tokens_out, created_at_utc, error_message
            ) VALUES (
                @id, @wi, @proj, @tpl, @prov, @model,
                @status, @valid, @miss,
                @spec, @prompt,
                @dur, @tin, @tout, @created, @err
            )
            """;
        cmd.Parameters.AddWithValue("@id", attempt.Id);
        cmd.Parameters.AddWithValue("@wi", attempt.WorkItemId);
        cmd.Parameters.AddWithValue("@proj", attempt.Project);
        cmd.Parameters.AddWithValue("@tpl", attempt.TemplateId);
        cmd.Parameters.AddWithValue("@prov", attempt.ProviderId);
        cmd.Parameters.AddWithValue("@model", attempt.Model);
        cmd.Parameters.AddWithValue("@status", AiStatusToString[attempt.Status]);
        cmd.Parameters.AddWithValue("@valid", attempt.ValidationPassed ? 1 : 0);
        cmd.Parameters.AddWithValue("@miss", string.Join(",", attempt.MissingSections));
        cmd.Parameters.AddWithValue("@spec", attempt.SpecFilePath);
        cmd.Parameters.AddWithValue("@prompt", attempt.PromptFilePath);
        cmd.Parameters.AddWithValue("@dur", attempt.DurationMs);
        cmd.Parameters.AddWithValue("@tin", attempt.TokensIn);
        cmd.Parameters.AddWithValue("@tout", attempt.TokensOut);
        cmd.Parameters.AddWithValue("@created", attempt.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@err", (object?)attempt.ErrorMessage ?? DBNull.Value);
        await NonQueryRetryAsync(cmd, ct);
    }
    finally { _lock.Release(); }
}

public async Task<IReadOnlyList<AiAttempt>> GetAiAttemptsForWorkItemAsync(int workItemId, CancellationToken ct = default)
{
    await _lock.WaitAsync(ct);
    try
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, work_item_id, project, template_id, provider_id, model,
                status, validation_passed, missing_sections,
                spec_file_path, prompt_file_path,
                duration_ms, tokens_in, tokens_out, created_at_utc, error_message
            FROM ai_attempts
            WHERE work_item_id = @wi
            ORDER BY created_at_utc DESC
            """;
        cmd.Parameters.AddWithValue("@wi", workItemId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<AiAttempt>();
        while (await reader.ReadAsync(ct))
        {
            var ordErr = reader.GetOrdinal("error_message");
            list.Add(new AiAttempt
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                WorkItemId = reader.GetInt32(reader.GetOrdinal("work_item_id")),
                Project = reader.GetString(reader.GetOrdinal("project")),
                TemplateId = reader.GetString(reader.GetOrdinal("template_id")),
                ProviderId = reader.GetString(reader.GetOrdinal("provider_id")),
                Model = reader.GetString(reader.GetOrdinal("model")),
                Status = ParseAiStatus(reader.GetString(reader.GetOrdinal("status"))),
                ValidationPassed = reader.GetInt32(reader.GetOrdinal("validation_passed")) == 1,
                MissingSections = [.. reader.GetString(reader.GetOrdinal("missing_sections"))
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                SpecFilePath = reader.GetString(reader.GetOrdinal("spec_file_path")),
                PromptFilePath = reader.GetString(reader.GetOrdinal("prompt_file_path")),
                DurationMs = reader.GetInt32(reader.GetOrdinal("duration_ms")),
                TokensIn = reader.GetInt32(reader.GetOrdinal("tokens_in")),
                TokensOut = reader.GetInt32(reader.GetOrdinal("tokens_out")),
                CreatedAtUtc = ParseStoredDate(reader, "created_at_utc") ?? DateTimeOffset.MinValue,
                ErrorMessage = reader.IsDBNull(ordErr) ? null : reader.GetString(ordErr)
            });
        }
        return list;
    }
    finally { _lock.Release(); }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~SqliteAiAttemptStoreTests"`
Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 6: Commit**

```bash
git add DevPulse.Core/Interfaces/IStateStore.cs DevPulse.Infrastructure/Persistence/SqliteStateStore.cs DevPulse.Tests/SqliteAiAttemptStoreTests.cs
git commit -m "feat(ai): SqliteStateStore implements ai_attempts record/retrieve"
```

---

### Task 8: Track `first_seen_utc` in UpsertWorkItemsAsync

**Files:**
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs`
- Modify: `DevPulse.Core/Models/WorkItem.cs`
- Create: `DevPulse.Tests/WorkItemFirstSeenTests.cs`

- [ ] **Step 1: Add `FirstSeenUtc` to `WorkItem` model**

In `DevPulse.Core/Models/WorkItem.cs`, add:

```csharp
public DateTimeOffset? FirstSeenUtc { get; set; }
```

- [ ] **Step 2: Write the failing test**

Create `DevPulse.Tests/WorkItemFirstSeenTests.cs`:

```csharp
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class WorkItemFirstSeenTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-fs-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    private static WorkItem MakeItem(int id, string state = "New") => new()
    {
        Id = id,
        Title = $"Item {id}",
        Type = WorkItemType.Bug,
        State = state,
        StateChangedAtUtc = DateTimeOffset.UtcNow,
        DiscoveredAtUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Insert_SetsFirstSeenUtc()
    {
        await _store.UpsertWorkItemsAsync([MakeItem(1)]);
        var items = await _store.GetWorkItemsAsync();
        items.Single().FirstSeenUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Upsert_DoesNotOverwriteFirstSeenUtc()
    {
        await _store.UpsertWorkItemsAsync([MakeItem(1)]);
        var firstSeen = (await _store.GetWorkItemsAsync()).Single().FirstSeenUtc;
        await Task.Delay(50);
        // Upsert the same item (state change simulates re-poll)
        await _store.UpsertWorkItemsAsync([MakeItem(1, "Active")]);
        var updated = (await _store.GetWorkItemsAsync()).Single();
        updated.State.Should().Be("Active");
        updated.FirstSeenUtc.Should().Be(firstSeen);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~WorkItemFirstSeenTests"`
Expected: `FirstSeenUtc` is always null.

- [ ] **Step 4: Update `UpsertWorkItemsAsync` to stamp `first_seen_utc` on insert only**

In `SqliteStateStore.cs`, locate the `UpsertWorkItemsAsync` method. Update its SQL and the loop:

Change the `INSERT` column list to include `first_seen_utc`:

```csharp
cmd.CommandText = """
    INSERT INTO work_items (
        id, title, item_type, state, board_column, priority,
        assigned_to_display, assigned_to_canonical, area_path, iteration_path, work_item_url,
        linked_pr_id, state_changed_at, days_in_state, aging_level, discovered_at, first_seen_utc
    ) VALUES (
        @id, @title, @type, @state, @col, @pri,
        @adisplay, @acanon, @area, @iter, @url,
        @linkedpr, @statedt, @days, @aging, @disc, @firstseen
    )
    ON CONFLICT(id) DO UPDATE SET
        title=excluded.title, item_type=excluded.item_type, state=excluded.state,
        board_column=excluded.board_column, priority=excluded.priority,
        assigned_to_display=excluded.assigned_to_display, assigned_to_canonical=excluded.assigned_to_canonical,
        area_path=excluded.area_path, iteration_path=excluded.iteration_path,
        work_item_url=excluded.work_item_url, linked_pr_id=excluded.linked_pr_id,
        state_changed_at=excluded.state_changed_at, days_in_state=excluded.days_in_state,
        aging_level=excluded.aging_level, discovered_at=excluded.discovered_at
    """;
```

Note: `first_seen_utc` is in the INSERT list but NOT in the UPDATE SET — this preserves the original value on upsert.

Add the parameter in the dummy-init block:

```csharp
cmd.Parameters.AddWithValue("@firstseen", string.Empty);
```

In the `foreach` loop, populate it from UtcNow (SQLite's `ON CONFLICT DO UPDATE` won't touch it, so it's only used on insert):

```csharp
cmd.Parameters["@firstseen"].Value = DateTimeOffset.UtcNow.ToString("O");
```

Update `ReadWorkItem` to read the column. Near the other field reads, add:

```csharp
var ordFirstSeen = r.GetOrdinal("first_seen_utc");
```

And in the returned `WorkItem` initializer:

```csharp
FirstSeenUtc = r.IsDBNull(ordFirstSeen) ? null : ParseStoredDate(r, "first_seen_utc")
```

Update the `GetWorkItemsAsync` SELECT list to include `first_seen_utc`:

```sql
SELECT id, title, item_type, state, board_column, priority,
    assigned_to_display, assigned_to_canonical, area_path, iteration_path,
    work_item_url, linked_pr_id, state_changed_at, days_in_state,
    aging_level, discovered_at, first_seen_utc
FROM work_items
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~WorkItemFirstSeenTests"`
Expected: both tests pass.

Run full suite: `dotnet test --nologo --no-build`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add DevPulse.Core/Models/WorkItem.cs DevPulse.Infrastructure/Persistence/SqliteStateStore.cs DevPulse.Tests/WorkItemFirstSeenTests.cs
git commit -m "feat(ai): stamp work_items.first_seen_utc on insert, preserve on upsert"
```

---

## Phase 4 — SecretStore generalization

### Task 9: Generic named-secret support in SecretStore

**Files:**
- Modify: `DevPulse.Infrastructure/Security/SecretStore.cs`

- [ ] **Step 1: Read current SecretStore**

Run: `cat DevPulse.Infrastructure/Security/SecretStore.cs`

Note the existing API (likely `TryLoadPat()` / `SavePat(string)`). We'll add named variants and make the old ones thin wrappers.

- [ ] **Step 2: Add generic named-secret methods**

Edit `DevPulse.Infrastructure/Security/SecretStore.cs`. Assuming current shape uses a constant path for PAT, generalize:

```csharp
using System.Security.Cryptography;
using System.Text;
using DevPulse.Core.Models;

namespace DevPulse.Infrastructure.Security;

public static class SecretStore
{
    private static string DirPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevPulse");

    private static string PathFor(string name) => Path.Combine(DirPath, $"{name}.dpapi");

    public static PatLoadResult TryLoadSecret(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return PatLoadResult.Missing();
        try
        {
            var enc = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return PatLoadResult.Ok(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            return PatLoadResult.Corrupt(ex.Message);
        }
    }

    public static void SaveSecret(string name, string value)
    {
        Directory.CreateDirectory(DirPath);
        var plain = Encoding.UTF8.GetBytes(value);
        var enc = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(name), enc);
    }

    // Backward-compat wrappers
    public static PatLoadResult TryLoadPat() => TryLoadSecret("pat");
    public static void SavePat(string value) => SaveSecret("pat", value);

    public static string? LoadPat() => TryLoadPat() is { IsOk: true, Value: var v } ? v : null;
}
```

Keep the existing `PatLoadResult` type as-is. If the current file uses a different shape, adapt — but keep method names `TryLoadPat`/`SavePat` as wrappers so existing call sites in `SettingsForm`, `TrayApplicationContext`, `Program.cs` keep compiling.

- [ ] **Step 3: Build**

Run: `dotnet build --nologo`
Expected: 0 errors.

- [ ] **Step 4: Full test suite**

Run: `dotnet test --nologo --no-build`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/Security/SecretStore.cs
git commit -m "feat(ai): generalize SecretStore to support named secrets (PAT + OpenRouter key)"
```

---

## Phase 5 — Filesystem writer (TDD)

### Task 10: FilesystemSpecWriter — TDD

**Files:**
- Create: `DevPulse.Infrastructure/Ai/FilesystemSpecWriter.cs`
- Create: `DevPulse.Tests/FilesystemSpecWriterTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class FilesystemSpecWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devpulse-fs-{Guid.NewGuid():N}");
    private readonly FilesystemSpecWriter _sut = new();

    [Fact]
    public async Task WriteAsync_CreatesVersionedFiles()
    {
        var ts = new DateTimeOffset(2026, 4, 21, 14, 30, 22, TimeSpan.Zero);
        var paths = await _sut.WriteAsync(_root, "MyProject", 42, ts,
            "## Spec\nbody", "prompt body", []);

        File.Exists(paths.SpecPath).Should().BeTrue();
        File.Exists(paths.PromptPath).Should().BeTrue();
        File.Exists(paths.MetaPath).Should().BeTrue();
        paths.SpecPath.Should().EndWith("spec-20260421T143022Z.md");
        paths.PromptPath.Should().EndWith("prompt-20260421T143022Z.md");
        await File.ReadAllTextAsync(paths.SpecPath).ContinueWith(t => t.Result.Should().Contain("## Spec"));
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfMissing()
    {
        var target = Path.Combine(_root, "NewProj", "99");
        Directory.Exists(target).Should().BeFalse();
        await _sut.WriteAsync(_root, "NewProj", 99, DateTimeOffset.UtcNow,
            "## s\nb", "p", []);
        Directory.Exists(target).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_SlugifiesProjectName()
    {
        await _sut.WriteAsync(_root, "My Project: With/Bad Chars", 1,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        Directory.GetDirectories(_root).Should().ContainSingle()
            .Which.Should().EndWith("My_Project__With_Bad_Chars");
    }

    [Fact]
    public async Task WriteAsync_RejectsEmptySlug()
    {
        var act = () => _sut.WriteAsync(_root, "", 1,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_RejectsPathTraversalInSlug()
    {
        var act = () => _sut.WriteAsync(_root, @"..\..\windows", 1,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_RejectsNonPositiveWorkItemId()
    {
        var act = () => _sut.WriteAsync(_root, "Proj", 0,
            DateTimeOffset.UtcNow, "## s\nb", "p", []);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_MetaJsonIsValidJsonArray()
    {
        var attempts = new List<AiAttempt>
        {
            new() { Id = "a1", WorkItemId = 1, Status = AiAttemptStatus.Success,
                    CreatedAtUtc = DateTimeOffset.UtcNow }
        };
        var paths = await _sut.WriteAsync(_root, "P", 1, DateTimeOffset.UtcNow,
            "## s\nb", "p", attempts);
        var json = await File.ReadAllTextAsync(paths.MetaPath);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FilesystemSpecWriterTests"`
Expected: build fails — `FilesystemSpecWriter` not defined.

- [ ] **Step 3: Implement FilesystemSpecWriter**

Create `DevPulse.Infrastructure/Ai/FilesystemSpecWriter.cs`:

```csharp
using System.Text.Json;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Infrastructure.Ai;

public sealed class FilesystemSpecWriter : IAiSpecWriter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public async Task<AiFilePaths> WriteAsync(
        string outputRoot,
        string projectSlug,
        int workItemId,
        DateTimeOffset timestampUtc,
        string specMarkdown,
        string promptMarkdown,
        IReadOnlyList<AiAttempt> attemptHistory,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
            throw new ArgumentException("Output root is required", nameof(outputRoot));
        if (workItemId <= 0)
            throw new ArgumentException("Work item id must be positive", nameof(workItemId));

        var slug = Slugify(projectSlug);
        if (string.IsNullOrEmpty(slug))
            throw new ArgumentException("Project slug cannot be empty after slugify", nameof(projectSlug));

        var rootFull = Path.GetFullPath(outputRoot);
        var candidate = Path.GetFullPath(Path.Combine(rootFull, slug, workItemId.ToString()));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Resolved path escapes output root", nameof(projectSlug));

        Directory.CreateDirectory(candidate);

        var tsStamp = timestampUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var specPath = Path.Combine(candidate, $"spec-{tsStamp}.md");
        var promptPath = Path.Combine(candidate, $"prompt-{tsStamp}.md");
        var metaPath = Path.Combine(candidate, "meta.json");

        await File.WriteAllTextAsync(specPath, specMarkdown, ct);
        await File.WriteAllTextAsync(promptPath, promptMarkdown, ct);
        var metaJson = JsonSerializer.Serialize(attemptHistory, JsonOpts);
        await File.WriteAllTextAsync(metaPath, metaJson, ct);

        return new AiFilePaths(specPath, promptPath, metaPath);
    }

    internal static string Slugify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var c in input)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FilesystemSpecWriterTests"`
Expected: `Passed! - Failed: 0`

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/Ai/FilesystemSpecWriter.cs DevPulse.Tests/FilesystemSpecWriterTests.cs
git commit -m "feat(ai): FilesystemSpecWriter with versioned output and path-traversal guards"
```

---

## Phase 6 — Providers

### Task 11: OpenRouterProvider — TDD with HttpMessageHandler mock

**Files:**
- Create: `DevPulse.Infrastructure/Ai/OpenRouterProvider.cs`
- Create: `DevPulse.Tests/OpenRouterProviderTests.cs`

- [ ] **Step 1: Write failing tests**

Create `DevPulse.Tests/OpenRouterProviderTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class OpenRouterProviderTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHandler(responder);
        return new HttpClient(handler);
    }

    private static string SuccessBody(string content) =>
        JsonSerializer.Serialize(new
        {
            model = "anthropic/claude-3.5-sonnet",
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 100, completion_tokens = 250 }
        });

    [Fact]
    public void Kind_IsHttp_PolicyIsCloud()
    {
        var sut = new OpenRouterProvider(MakeClient(_ => new HttpResponseMessage()), () => "key");
        sut.Kind.Should().Be(AiProviderKind.Http);
        sut.DataPolicy.Should().Be(AiDataPolicy.Cloud);
        sut.Id.Should().Be("openrouter");
    }

    [Fact]
    public async Task GenerateAsync_SuccessReturnsMarkdown()
    {
        var http = MakeClient(req =>
        {
            req.RequestUri!.ToString().Should().Contain("openrouter.ai/api/v1/chat/completions");
            req.Headers.Authorization!.Parameter.Should().Be("my-key");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SuccessBody("## Result\nbody"), Encoding.UTF8, "application/json")
            };
        });
        var sut = new OpenRouterProvider(http, () => "my-key");

        var result = await sut.GenerateAsync(new AiGenerateRequest("p", "model", TimeSpan.FromSeconds(30)));

        result.Markdown.Should().Contain("## Result");
        result.TokensIn.Should().Be(100);
        result.TokensOut.Should().Be(250);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_401SurfacesAuthError()
    {
        var http = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        { Content = new StringContent("{\"error\":\"bad key\"}") });
        var sut = new OpenRouterProvider(http, () => "bad");

        var act = () => sut.GenerateAsync(new AiGenerateRequest("p", "m", TimeSpan.FromSeconds(5)));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().Contain("401");
    }

    [Fact]
    public async Task HealthCheckAsync_NoKeyReturnsNotOk()
    {
        var http = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new OpenRouterProvider(http, () => "");

        var h = await sut.HealthCheckAsync();

        h.Ok.Should().BeFalse();
        h.ErrorMessage.Should().Contain("API key");
    }

    [Fact]
    public async Task GenerateAsync_NoKeyThrows()
    {
        var http = MakeClient(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = new OpenRouterProvider(http, () => "");

        var act = () => sut.GenerateAsync(new AiGenerateRequest("p", "m", TimeSpan.FromSeconds(1)));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _r;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> r) => _r = r;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(_r(request));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~OpenRouterProviderTests"`
Expected: build fails — `OpenRouterProvider` not defined.

- [ ] **Step 3: Implement OpenRouterProvider**

Create `DevPulse.Infrastructure/Ai/OpenRouterProvider.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Infrastructure.Ai;

public sealed class OpenRouterProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKeyProvider;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public OpenRouterProvider(HttpClient http, Func<string?> apiKeyProvider)
    {
        _http = http;
        _apiKeyProvider = apiKeyProvider;
    }

    public string Id => "openrouter";
    public string DisplayName => "OpenRouter";
    public AiProviderKind Kind => AiProviderKind.Http;
    public AiDataPolicy DataPolicy => AiDataPolicy.Cloud;

    public Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default)
    {
        var key = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult(new AiHealthResult(false, "OpenRouter API key not configured"));
        return Task.FromResult(new AiHealthResult(true, null));
    }

    public async Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct = default)
    {
        var key = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("OpenRouter API key not configured");

        var sw = Stopwatch.StartNew();
        var payload = new
        {
            model = req.Model,
            messages = new[] { new { role = "user", content = req.Prompt } }
        };
        var body = JsonSerializer.Serialize(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(req.Timeout);

        using var resp = await _http.SendAsync(request, linkedCts.Token);
        var respBody = await resp.Content.ReadAsStringAsync(linkedCts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter HTTP {(int)resp.StatusCode}: {respBody}", null, resp.StatusCode);

        var parsed = JsonSerializer.Deserialize<OrResponse>(respBody, JsonOpts)
            ?? throw new HttpRequestException("OpenRouter: unexpected null response body");
        var text = parsed.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new HttpRequestException("OpenRouter: no content in response");

        return new AiGenerateResult(
            Markdown: text,
            ModelUsed: parsed.Model ?? req.Model,
            TokensIn: parsed.Usage?.Prompt_tokens ?? 0,
            TokensOut: parsed.Usage?.Completion_tokens ?? 0,
            Duration: sw.Elapsed,
            ErrorMessage: null);
    }

    private sealed class OrResponse
    {
        public string? Model { get; set; }
        public List<OrChoice>? Choices { get; set; }
        public OrUsage? Usage { get; set; }
    }
    private sealed class OrChoice { public OrMessage? Message { get; set; } }
    private sealed class OrMessage { public string? Content { get; set; } }
    private sealed class OrUsage { public int Prompt_tokens { get; set; } public int Completion_tokens { get; set; } }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~OpenRouterProviderTests"`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/Ai/OpenRouterProvider.cs DevPulse.Tests/OpenRouterProviderTests.cs
git commit -m "feat(ai): OpenRouterProvider with HTTPS auth, timeout, and error surfacing"
```

---

### Task 12: ClaudeCliProvider with fake .cmd fixtures — TDD

**Files:**
- Create: `DevPulse.Infrastructure/Ai/ClaudeCliProvider.cs`
- Create: `DevPulse.Tests/Fixtures/FakeCliEcho.cmd`
- Create: `DevPulse.Tests/Fixtures/FakeCliFail.cmd`
- Create: `DevPulse.Tests/ClaudeCliProviderTests.cs`
- Modify: `DevPulse.Tests/DevPulse.Tests.csproj` (copy fixtures to output)

- [ ] **Step 1: Create fixture scripts**

`DevPulse.Tests/Fixtures/FakeCliEcho.cmd`:

```bat
@echo off
setlocal enabledelayedexpansion
set "line="
for /f "delims=" %%L in ('more') do (
    if defined line (echo(!line!)
    set "line=%%L"
)
if defined line echo(!line!
```

Simpler version — Windows `more` behaves oddly with stdin in tests. Use PowerShell wrapping in the provider test? Actually, we'll use a simpler script that emits a fixed markdown:

`DevPulse.Tests/Fixtures/FakeCliEcho.cmd`:

```bat
@echo off
echo ## Context summary
echo Fake content from test fixture.
echo ## Functional requirements
echo FR1
echo ## Acceptance criteria
echo G/W/T
echo ## Edge cases
echo None
echo ## Test plan
echo Run it
echo ## Risks and dependencies
echo None
```

`DevPulse.Tests/Fixtures/FakeCliFail.cmd`:

```bat
@echo off
echo something went wrong 1>&2
exit /b 2
```

- [ ] **Step 2: Copy fixtures to test output**

In `DevPulse.Tests/DevPulse.Tests.csproj`, add inside the main `<Project>` (create an `<ItemGroup>` if needed):

```xml
<ItemGroup>
  <None Include="Fixtures\FakeCliEcho.cmd">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
  <None Include="Fixtures\FakeCliFail.cmd">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

- [ ] **Step 3: Write failing tests**

```csharp
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class ClaudeCliProviderTests
{
    private static string EchoFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FakeCliEcho.cmd");
    private static string FailFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "FakeCliFail.cmd");

    [Fact]
    public void Kind_IsCli_PolicyIsLocal()
    {
        var sut = new ClaudeCliProvider(EchoFixture);
        sut.Kind.Should().Be(AiProviderKind.Cli);
        sut.DataPolicy.Should().Be(AiDataPolicy.Local);
        sut.Id.Should().Be("claude-cli");
    }

    [Fact]
    public async Task HealthCheck_FixtureExists_Ok()
    {
        var sut = new ClaudeCliProvider(EchoFixture);
        (await sut.HealthCheckAsync()).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task HealthCheck_PathMissing_NotOk()
    {
        var sut = new ClaudeCliProvider(@"C:\does\not\exist\claude.exe");
        (await sut.HealthCheckAsync()).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_EchoFixtureReturnsMarkdown()
    {
        var sut = new ClaudeCliProvider(EchoFixture);
        var result = await sut.GenerateAsync(
            new AiGenerateRequest("anything", "model", TimeSpan.FromSeconds(10)));
        result.Markdown.Should().Contain("## Context summary");
        result.Markdown.Should().Contain("## Test plan");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_FailingFixtureThrows()
    {
        var sut = new ClaudeCliProvider(FailFixture);
        var act = () => sut.GenerateAsync(new AiGenerateRequest("x", "m", TimeSpan.FromSeconds(5)));
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("exit code 2");
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ClaudeCliProviderTests"`
Expected: build fails — `ClaudeCliProvider` not defined.

- [ ] **Step 5: Implement ClaudeCliProvider**

Create `DevPulse.Infrastructure/Ai/ClaudeCliProvider.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Infrastructure.Ai;

public sealed class ClaudeCliProvider : IAiProvider
{
    private readonly string _executablePath;

    public ClaudeCliProvider(string executablePath)
    {
        _executablePath = executablePath ?? string.Empty;
    }

    public string Id => "claude-cli";
    public string DisplayName => "Claude Code CLI";
    public AiProviderKind Kind => AiProviderKind.Cli;
    public AiDataPolicy DataPolicy => AiDataPolicy.Local;

    public Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
            return Task.FromResult(new AiHealthResult(false, $"Executable not found: {_executablePath}"));
        return Task.FromResult(new AiHealthResult(true, null));
    }

    public async Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct = default)
    {
        if (!File.Exists(_executablePath))
            throw new InvalidOperationException($"Claude CLI not found at {_executablePath}");

        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = "--print",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.StandardInput.WriteAsync(req.Prompt); }
        finally { proc.StandardInput.Close(); }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(req.Timeout);

        try
        {
            await proc.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Claude CLI exceeded timeout {req.Timeout.TotalSeconds}s");
        }

        var exitCode = proc.ExitCode;
        var outText = stdout.ToString();
        var errText = stderr.ToString();

        if (exitCode != 0)
        {
            var tail = errText.Length > 500 ? errText[^500..] : errText;
            throw new InvalidOperationException($"Claude CLI exit code {exitCode}: {tail}");
        }

        if (string.IsNullOrWhiteSpace(outText))
            throw new InvalidOperationException("Claude CLI returned no output");

        return new AiGenerateResult(
            Markdown: outText,
            ModelUsed: "",
            TokensIn: 0,
            TokensOut: 0,
            Duration: sw.Elapsed,
            ErrorMessage: null);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ClaudeCliProviderTests"`
Expected: all 5 pass.

- [ ] **Step 7: Commit**

```bash
git add DevPulse.Infrastructure/Ai/ClaudeCliProvider.cs DevPulse.Tests/Fixtures/ DevPulse.Tests/DevPulse.Tests.csproj DevPulse.Tests/ClaudeCliProviderTests.cs
git commit -m "feat(ai): ClaudeCliProvider with Process spawning, timeout kill, stderr capture"
```

---

## Phase 7 — Settings integration

The remaining phases are broken into a second plan document to keep each file focused. See `2026-04-21-ai-action-pipeline-part2.md` for:

- **Task 13:** `AppSettings.AiOutputRootPath` field
- **Task 14:** `SettingsAiTemplateStore` (IAiTemplateStore backed by SettingsService)
- **Task 15:** Default shipped templates (Bug, User Story, Feature, Task)
- **Task 16:** `SettingsService.SaveAiConfigAsync` atomic write
- **Task 17:** `AiPipelineService` orchestrator (TDD)
- **Task 18:** `MarkdownToRtfRenderer` (H1–H4, lists, code blocks, bold/italic)
- **Task 19:** `AiGenerateDialog` WinForm
- **Task 20:** `AiReviewForm` WinForm
- **Task 21:** `SettingsForm` AI tab (fields, template editor)
- **Task 22:** `BoardForm` context menu items + eligibility gating
- **Task 23:** `TrayApplicationContext` wire-up
- **Task 24:** Smoke test checklist

---

## Self-Review (Part 1 of plan)

1. **Spec coverage (Phases 1–6):** Core enums/models/interfaces (§1, §3) ✓. AiOutputValidator (§3) ✓. AiTemplateRenderer (§3) ✓. Schema v2 migration (§2) ✓. AI attempts table read/write (§2, §3) ✓. first_seen_utc tracking (§2, §4) ✓. Generic SecretStore (§6) ✓. FilesystemSpecWriter (§3) ✓. OpenRouterProvider (§3, §5) ✓. ClaudeCliProvider (§3, §5, §6) ✓. Phase 7 (settings/templates/pipeline/UI) deferred to part 2 — marked clearly.
2. **Placeholder scan:** No TBDs/TODOs in the tasks. Each task has full test code and full implementation code.
3. **Type consistency:** `AiAttempt.MissingSections` is `List<string>` in the model, round-tripped through comma-separated string in DB (consistent across Task 2, Task 7). `AiFilePaths` is a record with three fields, used identically in Task 3 and Task 10. `IAiProvider` signature matches across Tasks 3, 11, 12.
4. **Scope check:** Part 1 covers Core + Infrastructure + migration + providers — all unit-testable, no UI dependencies, no cross-project wiring. Part 2 covers App services + WinForms UI + wire-up. Natural seam. Each part ships as a working incremental milestone.

Part 1 is focused enough for a single implementation pass. Phase 7 will be a second plan doc.
