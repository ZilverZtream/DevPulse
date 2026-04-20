# DevPulse Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all 29 findings from the second security/quality audit without introducing new bugs.

**Architecture:** Tasks are ordered by dependency layer — Core enums first (rename globally), then Core logic, then Core models, then Infrastructure, then App-layer pollers, then Forms, then API/performance cleanup. Each task is self-contained and can be verified independently before committing.

**Tech Stack:** .NET 8 WinForms, C# 12, SQLite via Microsoft.Data.Sqlite, Serilog, xUnit

---

## ⚠ Architectural Items — OUT OF SCOPE for This Plan

These four findings require a separate brainstorm + plan. Do not implement them here.

| # | Finding | Why Separate |
|---|---------|--------------|
| 14 | TrayApplicationContext god object | Splits into PollingOrchestrator + FormManager — touches 15+ callsites |
| 15 | Inbox stable IDs | Requires DB schema migration + cascading rename across all inbox name keys |
| 17 | IStateStore unrelated concerns (split into IEventStore, IWorkItemStore, IMuteStore, IPollStateStore) | Breaks every test, every caller |
| 28 | Schema version/migration runner | Must be done before any other schema change lands; is the foundation for 15 |

---

## File Structure

| File | Change |
|------|--------|
| `DevPulse.Core/Enums/AgingLevel.cs` | Rename `Aging` → `Warning` |
| `DevPulse.Core/Enums/WatcherType.cs` | Rename `WorkItemType` → `ByWorkItemType`, `WorkItemState` → `ByWorkItemState` |
| `DevPulse.Core/Enums/EventSource.cs` | Rename enum `EventSource` → `PrEventSource` |
| `DevPulse.Core/Services/WorkItemNormalizer.cs` | Clamp negative days; update `AgingLevel.Warning` reference |
| `DevPulse.Core/Services/RuleEngine.cs` | Filter empty keywords in `ExpandKeywords` |
| `DevPulse.Core/Services/IdentityNormalizer.cs` | Guard null `DisplayName` in `ClassifySource` |
| `DevPulse.Core/Services/EventCollapser.cs` | Remove explicit `IsCollapsed = true` assignments |
| `DevPulse.Core/Models/DevOpsEvent.cs` | Make `IsCollapsed` a computed property |
| `DevPulse.Core/Models/MuteEntry.cs` | Add `PrId int?` and `AuthorKey string` typed fields |
| `DevPulse.Core/Services/MuteService.cs` | Use typed fields; update factory methods |
| `DevPulse.Core/Interfaces/IStateStore.cs` | Update `RemoveMuteEntryAsync` signature |
| `DevPulse.Infrastructure/Persistence/DbSchema.cs` | Add `pr_id`/`author_key` columns to `mute_entries`; add inline migration |
| `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs` | `ParseStoredDate` → nullable; update mute read/write; fix `IsCollapsed` write |
| `DevPulse.Infrastructure/Notifications/WindowsToastNotificationService.cs` | Replace bare `catch` with specific exceptions + Serilog |
| `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs` | Classify 401/403/429; paginate PR list |
| `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsAuthHandler.cs` | New file: DelegatingHandler that injects PAT header for ADO host only |
| `DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs` | New file: shared retry logic extracted from both clients |
| `DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs` | Validate area/iteration path patterns; enforce `$top`; use `AdoRetryHelper` |
| `DevPulse.App/Services/PollingLoopBase.cs` | Implement `IAsyncDisposable`; await `_loopTask` on dispose |
| `DevPulse.App/Services/PollingService.cs` | Guard duplicate/null reviewer Id; guard null `UniqueName` |
| `DevPulse.App/Forms/SettingsForm.cs` | Top-level try/catch on async event handlers; fix fire-and-forget; validation |
| `DevPulse.App/Forms/BoardForm.cs` | Pass `filteredItems` (not `_allItems`) to `GroupByColumn` |
| `DevPulse.App/TrayApplicationContext.cs` | Incremental tray menu update (skip rebuild when counts unchanged) |
| `DevPulse.Tests/CoreLogicTests.cs` | New file: unit tests for all Core logic changes |

---

## Task 1: Rename `AgingLevel.Aging` → `AgingLevel.Warning`

**Files:**
- Modify: `DevPulse.Core/Enums/AgingLevel.cs`
- Modify: `DevPulse.Core/Services/WorkItemNormalizer.cs` (line 66–70)
- Modify: `DevPulse.App/UI/BoardColumnPanel.cs` (anywhere `AgingLevel.Aging` is used for color)

- [ ] **Step 1: Change the enum value**

In `DevPulse.Core/Enums/AgingLevel.cs`, change:
```csharp
public enum AgingLevel { Fresh, Aging, Stale }
```
to:
```csharp
public enum AgingLevel { Fresh, Warning, Stale }
```

- [ ] **Step 2: Build to find all references that now fail to compile**

```
dotnet build DevPulse.sln
```
Expected: compile errors listing every `AgingLevel.Aging` reference.

- [ ] **Step 3: Fix `WorkItemNormalizer.cs`**

In `ComputeAging` (line 66), change `AgingLevel.Aging` → `AgingLevel.Warning`:
```csharp
private static AgingLevel ComputeAging(int days, BoardColumnDefinition col)
{
    if (days >= col.AgingDaysStale) return AgingLevel.Stale;
    if (days >= col.AgingDaysWarning) return AgingLevel.Warning;
    return AgingLevel.Fresh;
}
```

- [ ] **Step 4: Fix all remaining compile errors** (grep for `AgingLevel.Aging` and replace)

```
grep -rn "AgingLevel.Aging" --include="*.cs" .
```
Replace each occurrence with `AgingLevel.Warning`.

- [ ] **Step 5: Build to verify zero errors**

```
dotnet build DevPulse.sln
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add DevPulse.Core/Enums/AgingLevel.cs DevPulse.Core/Services/WorkItemNormalizer.cs
git add $(git diff --name-only | grep "\.cs$")
git commit -m "rename AgingLevel.Aging to Warning — Aging reads as a process not a state"
```

---

## Task 2: Rename `WatcherType.WorkItemType/WorkItemState` → `ByWorkItemType/ByWorkItemState`

**Files:**
- Modify: `DevPulse.Core/Enums/WatcherType.cs`
- Modify: `DevPulse.Core/Services/RuleEngine.cs` (switch expression in `MatchesWatcher`)

- [ ] **Step 1: Update the enum**

In `DevPulse.Core/Enums/WatcherType.cs`:
```csharp
public enum WatcherType
{
    Author,
    Repository,
    PrTitlePattern,
    ByWorkItemType,
    ByWorkItemState
}
```

- [ ] **Step 2: Build to find all references**

```
dotnet build DevPulse.sln
```
Expected: compile errors on `WatcherType.WorkItemType` and `WatcherType.WorkItemState`.

- [ ] **Step 3: Fix RuleEngine.cs `MatchesWatcher` switch**

The `WatcherType.WorkItemType` and `WatcherType.WorkItemState` cases fall through to `_ => false` already, so the switch expression compiles with just a rename:
```csharp
private static bool MatchesWatcher(DevOpsEvent evt, Watcher watcher) => watcher.Type switch
{
    WatcherType.Author => evt.AuthorCanonicalKey.Contains(watcher.Pattern, StringComparison.OrdinalIgnoreCase),
    WatcherType.Repository => evt.Repository.Equals(watcher.Pattern, StringComparison.OrdinalIgnoreCase),
    WatcherType.PrTitlePattern => MatchesGlob(evt.PullRequestTitle, watcher.Pattern),
    _ => false
};
```

- [ ] **Step 4: Fix any remaining compile errors** (grep for the old names)

```
grep -rn "WatcherType.WorkItemType\|WatcherType.WorkItemState" --include="*.cs" .
```

- [ ] **Step 5: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 6: Commit**

```bash
git add DevPulse.Core/Enums/WatcherType.cs DevPulse.Core/Services/RuleEngine.cs
git commit -m "rename WatcherType.WorkItemType/State to ByWorkItemType/ByWorkItemState — disambiguates from the WorkItemType model enum"
```

---

## Task 3: Rename `EventSource` enum → `PrEventSource`

This is the most pervasive rename — it touches all three projects. Do it in one pass.

**Files:**
- Rename: `DevPulse.Core/Enums/EventSource.cs` (file rename + enum rename)
- Modify: all `.cs` files that reference `EventSource`

- [ ] **Step 1: Rename the enum in the file**

In `DevPulse.Core/Enums/EventSource.cs`, change:
```csharp
namespace DevPulse.Core.Enums;

public enum PrEventSource { Unknown, Human, Bot, System }
```

Also rename the file to `PrEventSource.cs`.

- [ ] **Step 2: Build to find all broken references**

```
dotnet build DevPulse.sln
```
Expected: compile errors everywhere `EventSource` is used as a type.

- [ ] **Step 3: Global rename — find every reference**

```
grep -rn "\bEventSource\b" --include="*.cs" .
```

Files that need updating (based on the codebase):
- `DevPulse.Core/Models/DevOpsEvent.cs` — `public EventSource EventSource { get; set; }`
- `DevPulse.Core/Interfaces/IStateStore.cs` — no direct reference, but `DevOpsEvent` is used
- `DevPulse.Core/Services/RuleEngine.cs` — `EventSource.Bot`, `EventSource.System`, `EventSource.Human`, `rule.EventSourceEquals`
- `DevPulse.Core/Services/IdentityNormalizer.cs` — `return EventSource.Bot;`, `return EventSource.Human;`
- `DevPulse.Core/Models/InboxRule.cs` — `EventSource? EventSourceEquals`
- `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs` — `(EventSource)reader.GetInt32(...)`
- `DevPulse.App/Services/PollingService.cs` — all `EventSource = idNorm.ClassifySource(...)` assignments

- [ ] **Step 4: Apply renames — replace `EventSource` with `PrEventSource` in each file**

For each file found, replace the type name. Examples:

`DevPulse.Core/Models/DevOpsEvent.cs`:
```csharp
public PrEventSource EventSource { get; set; }
```

`DevPulse.Core/Services/RuleEngine.cs`:
```csharp
if (evt.EventSource == PrEventSource.Bot || evt.EventSource == PrEventSource.System) return false;
// and
if (rule.EventSourceEquals.HasValue && evt.EventSource != rule.EventSourceEquals.Value) return false;
```

`DevPulse.Core/Services/IdentityNormalizer.cs`:
```csharp
public PrEventSource ClassifySource(IdentityRefDto identity)
{
    // ...
    return PrEventSource.Bot;
    // ...
    return PrEventSource.Human;
}
```

`DevPulse.Core/Models/InboxRule.cs` (wherever `EventSource?` appears):
```csharp
public PrEventSource? EventSourceEquals { get; set; }
```

`DevPulse.Infrastructure/Persistence/SqliteStateStore.cs` in `ReadEvent`:
```csharp
EventSource = (PrEventSource)r.GetInt32(r.GetOrdinal("event_source")),
```

`DevPulse.App/Services/PollingService.cs` — update all `EventSource = ...` field assignments.

- [ ] **Step 5: Build to zero errors**

```
dotnet build DevPulse.sln
```

- [ ] **Step 6: Commit**

```bash
git add -u
git commit -m "rename EventSource enum to PrEventSource — avoids collision with System.Diagnostics.Tracing.EventSource"
```

---

## Task 4: WorkItemNormalizer — clamp negative days to 0

**Files:**
- Modify: `DevPulse.Core/Services/WorkItemNormalizer.cs` (line 35)
- Create: `DevPulse.Tests/CoreLogicTests.cs` (first test)

- [ ] **Step 1: Create the test file**

Create `DevPulse.Tests/CoreLogicTests.cs`:
```csharp
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Core.Services;

namespace DevPulse.Tests;

public class WorkItemNormalizerTests
{
    private static readonly IReadOnlyList<BoardColumnDefinition> NoColumns = [];

    [Fact]
    public void Normalize_FutureStateChangedDate_DaysIsZeroNotNegative()
    {
        var normalizer = new WorkItemNormalizer();
        var futureDate = DateTimeOffset.UtcNow.AddDays(2); // clock skew simulation
        var dto = new WorkItemDto
        {
            Id = 1,
            Title = "T",
            WorkItemType = "Task",
            State = "Active",
            StateChangedDate = futureDate
        };
        var now = DateTimeOffset.UtcNow;

        var item = normalizer.Normalize(dto, NoColumns, now);

        Assert.Equal(0, item.DaysInCurrentState);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj --filter "WorkItemNormalizerTests" -v minimal
```
Expected: FAIL — `DaysInCurrentState` is negative (e.g., -2).

- [ ] **Step 3: Fix WorkItemNormalizer**

In `DevPulse.Core/Services/WorkItemNormalizer.cs`, line 35, change:
```csharp
var days = (int)(now - stateChangedAt).TotalDays;
```
to:
```csharp
var days = Math.Max(0, (int)(now - stateChangedAt).TotalDays);
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj --filter "WorkItemNormalizerTests" -v minimal
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Services/WorkItemNormalizer.cs DevPulse.Tests/CoreLogicTests.cs
git commit -m "clamp WorkItemNormalizer.DaysInCurrentState to 0 minimum — clock skew can produce future state-change dates"
```

---

## Task 5: RuleEngine — filter empty/whitespace keywords in `ExpandKeywords`

**Files:**
- Modify: `DevPulse.Core/Services/RuleEngine.cs` (line 137–147)
- Modify: `DevPulse.Tests/CoreLogicTests.cs` (add test class)

- [ ] **Step 1: Add test**

Append to `DevPulse.Tests/CoreLogicTests.cs`:
```csharp
public class RuleEngineTests
{
    [Fact]
    public void AssignInbox_EmptyKeywordInMessageContainsAny_DoesNotMatchArbitraryMessage()
    {
        var rule = new InboxRule
        {
            Enabled = true,
            MessageContainsAny = ["", "   ", "nope"]  // blank entries should not act as wildcard
        };
        var inbox = new InboxDefinition
        {
            Name = "Test", IsEnabled = true, Order = 0, IsSystemInbox = false,
            Rules = [rule]
        };
        var evt = new DevOpsEvent
        {
            MessageText = "hello world",
            AuthorCanonicalKey = "user@corp.com",
            Status = "active", Repository = "repo", Project = "proj"
        };

        var result = new RuleEngine().AssignInbox(evt, [], [inbox], [], new AppSettings());

        Assert.Equal("Unassigned", result);  // "nope" is not in message; blanks must not match
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj --filter "RuleEngineTests" -v minimal
```
Expected: FAIL — the empty string in `MessageContainsAny` acts as a wildcard and causes the inbox to match.

- [ ] **Step 3: Fix `ExpandKeywords` in `RuleEngine.cs`**

Replace the method body (lines 137–147):
```csharp
private static IEnumerable<string> ExpandKeywords(IEnumerable<string> items, IReadOnlyList<KeywordPack> packs)
{
    foreach (var item in items)
    {
        var pack = packs.FirstOrDefault(p => p.Name.Equals(item, StringComparison.OrdinalIgnoreCase));
        if (pack != null)
        {
            foreach (var kw in pack.Keywords)
                if (!string.IsNullOrWhiteSpace(kw)) yield return kw;
        }
        else if (!string.IsNullOrWhiteSpace(item))
        {
            yield return item;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj --filter "RuleEngineTests" -v minimal
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Services/RuleEngine.cs DevPulse.Tests/CoreLogicTests.cs
git commit -m "filter empty/whitespace keywords in RuleEngine.ExpandKeywords — empty string matched every message"
```

---

## Task 6: IdentityNormalizer — guard null `DisplayName` in `ClassifySource`

**Files:**
- Modify: `DevPulse.Core/Services/IdentityNormalizer.cs` (lines 36–47)
- Modify: `DevPulse.Tests/CoreLogicTests.cs` (add test class)

- [ ] **Step 1: Add test**

Append to `DevPulse.Tests/CoreLogicTests.cs`:
```csharp
public class IdentityNormalizerTests
{
    [Fact]
    public void ClassifySource_NullDisplayName_DoesNotThrow()
    {
        var normalizer = new IdentityNormalizer([], ["bot"]);
        var identity = new IdentityRefDto { DisplayName = null!, UniqueName = "user@org.com" };

        var result = normalizer.ClassifySource(identity);  // must not throw NRE

        Assert.Equal(PrEventSource.Human, result);
    }

    [Fact]
    public void ClassifySource_BotPatternInDisplayName_ReturnsBot()
    {
        var normalizer = new IdentityNormalizer([], ["[bot]"]);
        var identity = new IdentityRefDto { DisplayName = "Renovate [bot]", UniqueName = string.Empty };

        var result = normalizer.ClassifySource(identity);

        Assert.Equal(PrEventSource.Bot, result);
    }
}
```

- [ ] **Step 2: Run tests to verify failure**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj --filter "IdentityNormalizerTests" -v minimal
```
Expected: first test FAILS with `NullReferenceException`.

- [ ] **Step 3: Fix `ClassifySource` in `IdentityNormalizer.cs`**

Replace lines 34–47:
```csharp
public PrEventSource ClassifySource(IdentityRefDto identity)
{
    var canonical = Normalize(identity);
    var display = identity.DisplayName ?? string.Empty;

    foreach (var pattern in _botPatterns)
    {
        if (canonical.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
            display.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            return PrEventSource.Bot;
    }

    return PrEventSource.Human;
}
```

- [ ] **Step 4: Run tests to verify both pass**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj --filter "IdentityNormalizerTests" -v minimal
```
Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Services/IdentityNormalizer.cs DevPulse.Tests/CoreLogicTests.cs
git commit -m "guard null DisplayName in IdentityNormalizer.ClassifySource — ADO API can return null displayName"
```

---

## Task 7: `DevOpsEvent.IsCollapsed` — enforce computed invariant

**Files:**
- Modify: `DevPulse.Core/Models/DevOpsEvent.cs`
- Modify: `DevPulse.Core/Services/EventCollapser.cs` (remove redundant `IsCollapsed = true`)
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs` (`ReadEvent` + `SaveEventsAsync`)

- [ ] **Step 1: Change `DevOpsEvent.IsCollapsed` to a computed property**

In `DevPulse.Core/Models/DevOpsEvent.cs`, replace:
```csharp
public bool IsCollapsed { get; set; }
public int CollapsedCount { get; set; } = 1;
```
with:
```csharp
public int CollapsedCount { get; set; } = 1;
public bool IsCollapsed => CollapsedCount > 1;
```

- [ ] **Step 2: Build to find compiler errors**

```
dotnet build DevPulse.sln
```
Expected: errors on any code that *assigns* to `IsCollapsed` (reads are fine — computed props satisfy reads).

- [ ] **Step 3: Fix `EventCollapser.cs`**

Find the line that sets `IsCollapsed = true` on the aggregated event and remove it. `CollapsedCount` is already set to `group.Count`, so `IsCollapsed` will compute correctly.

- [ ] **Step 4: Fix `SqliteStateStore.ReadEvent`**

Remove the line that reads `IsCollapsed` from the DB (it's now computed):
```csharp
// REMOVE this line:
IsCollapsed = r.GetInt32(r.GetOrdinal("is_collapsed")) == 1,
```

- [ ] **Step 5: Fix `SqliteStateStore.SaveEventsAsync`**

Update the parameter to compute from `CollapsedCount`:
```csharp
cmd.Parameters.AddWithValue("@collapsed", e.CollapsedCount > 1 ? 1 : 0);
```

- [ ] **Step 6: Build clean**

```
dotnet build DevPulse.sln
```
Expected: zero errors.

- [ ] **Step 7: Commit**

```bash
git add DevPulse.Core/Models/DevOpsEvent.cs DevPulse.Core/Services/EventCollapser.cs
git add DevPulse.Infrastructure/Persistence/SqliteStateStore.cs
git commit -m "make DevOpsEvent.IsCollapsed a computed property — was drifting from CollapsedCount"
```

---

## Task 8: `MuteEntry` typed fields — replace dual-purpose `Key` string

**Files:**
- Modify: `DevPulse.Core/Models/MuteEntry.cs`
- Modify: `DevPulse.Core/Services/MuteService.cs`
- Modify: `DevPulse.Core/Interfaces/IStateStore.cs` (`RemoveMuteEntryAsync` signature)
- Modify: `DevPulse.Infrastructure/Persistence/DbSchema.cs` (add columns + migration)
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs` (read/write mutes)

- [ ] **Step 1: Update `MuteEntry` model**

In `DevPulse.Core/Models/MuteEntry.cs`:
```csharp
using DevPulse.Core.Enums;

namespace DevPulse.Core.Models;

public sealed class MuteEntry
{
    public MuteScope Scope { get; set; }
    public int? PrId { get; set; }
    public string AuthorKey { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAtUtc { get; set; }

    // Derived: the DB primary key (scope, key) uses this for dedup.
    internal string DbKey => Scope == MuteScope.PullRequest
        ? (PrId?.ToString() ?? string.Empty)
        : AuthorKey;
}
```

- [ ] **Step 2: Update `MuteService.cs`**

Replace `IsMuted` and the static factory methods:
```csharp
public bool IsMuted(DevOpsEvent evt, IReadOnlyList<MuteEntry> activeMutes, DateTimeOffset now)
{
    foreach (var mute in activeMutes)
    {
        if (mute.ExpiresAtUtc.HasValue && mute.ExpiresAtUtc.Value <= now)
            continue;

        if (mute.Scope == MuteScope.PullRequest && mute.PrId == evt.PullRequestId)
            return true;

        if (mute.Scope == MuteScope.Author &&
            mute.AuthorKey.Equals(evt.AuthorCanonicalKey, StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}

public static MuteEntry CreatePrMute(int prId) => new()
{
    Scope = MuteScope.PullRequest,
    PrId = prId
};

public static MuteEntry CreatePrSnooze(int prId, DateTimeOffset expiresAt) => new()
{
    Scope = MuteScope.PullRequest,
    PrId = prId,
    ExpiresAtUtc = expiresAt
};

public static MuteEntry CreateAuthorMuteToday(string canonicalKey, DateTimeOffset now) => new()
{
    Scope = MuteScope.Author,
    AuthorKey = canonicalKey,
    ExpiresAtUtc = now.Date.AddDays(1)
};

public static MuteEntry CreateAuthorMutePermanent(string canonicalKey) => new()
{
    Scope = MuteScope.Author,
    AuthorKey = canonicalKey
};
```

- [ ] **Step 3: Update `IStateStore.RemoveMuteEntryAsync`**

In `DevPulse.Core/Interfaces/IStateStore.cs`, change the signature:
```csharp
Task RemoveMuteEntryAsync(MuteEntry entry, CancellationToken ct = default);
```

- [ ] **Step 4: Add columns to `DbSchema.cs`**

In `DevPulse.Infrastructure/Persistence/DbSchema.cs`, add a second command after the main `CREATE TABLE` block:
```csharp
public static async Task EnsureCreatedAsync(SqliteConnection conn)
{
    // ... existing CREATE TABLE block unchanged ...
    await cmd.ExecuteNonQueryAsync();

    // Inline migration: add typed mute columns (idempotent — fails silently if already present)
    foreach (var alter in new[]
    {
        "ALTER TABLE mute_entries ADD COLUMN pr_id INTEGER",
        "ALTER TABLE mute_entries ADD COLUMN author_key TEXT NOT NULL DEFAULT ''"
    })
    {
        try
        {
            await using var m = conn.CreateCommand();
            m.CommandText = alter;
            await m.ExecuteNonQueryAsync();
        }
        catch (SqliteException) { /* column already exists — safe to ignore */ }
    }

    // Back-fill typed columns from existing key column
    await using var backfill = conn.CreateCommand();
    backfill.CommandText = """
        UPDATE mute_entries SET pr_id = CAST(key AS INTEGER) WHERE scope = 0 AND pr_id IS NULL;
        UPDATE mute_entries SET author_key = key WHERE scope = 1 AND author_key = '';
        """;
    await backfill.ExecuteNonQueryAsync();
}
```

- [ ] **Step 5: Update `SqliteStateStore.SaveMuteEntryAsync`**

```csharp
public async Task SaveMuteEntryAsync(MuteEntry entry, CancellationToken ct = default)
{
    await _lock.WaitAsync(ct);
    try
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mute_entries (scope, key, expires_at, pr_id, author_key)
            VALUES (@scope, @key, @exp, @prid, @akey)
            ON CONFLICT(scope, key) DO UPDATE SET
                expires_at = excluded.expires_at,
                pr_id = excluded.pr_id,
                author_key = excluded.author_key
            """;
        cmd.Parameters.AddWithValue("@scope", (int)entry.Scope);
        cmd.Parameters.AddWithValue("@key", entry.DbKey);
        cmd.Parameters.AddWithValue("@exp", (object?)entry.ExpiresAtUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@prid", (object?)entry.PrId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@akey", entry.AuthorKey);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    finally { _lock.Release(); }
}
```

- [ ] **Step 6: Update `SqliteStateStore.RemoveMuteEntryAsync`**

Change signature to accept `MuteEntry`:
```csharp
public async Task RemoveMuteEntryAsync(MuteEntry entry, CancellationToken ct = default)
{
    await _lock.WaitAsync(ct);
    try
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM mute_entries WHERE scope = @scope AND key = @key";
        cmd.Parameters.AddWithValue("@scope", (int)entry.Scope);
        cmd.Parameters.AddWithValue("@key", entry.DbKey);
        await cmd.ExecuteNonQueryAsync(ct);
    }
    finally { _lock.Release(); }
}
```

- [ ] **Step 7: Update `SqliteStateStore.GetActiveMutesAsync`**

Read from the typed columns:
```csharp
list.Add(new MuteEntry
{
    Scope = (MuteScope)reader.GetInt32(0),
    PrId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
    AuthorKey = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
    ExpiresAtUtc = exp
});
```

Update the SELECT to include `pr_id, author_key`:
```csharp
cmd.CommandText = "SELECT scope, key, expires_at, pr_id, author_key FROM mute_entries";
```

- [ ] **Step 8: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 9: Commit**

```bash
git add DevPulse.Core/Models/MuteEntry.cs DevPulse.Core/Services/MuteService.cs
git add DevPulse.Core/Interfaces/IStateStore.cs
git add DevPulse.Infrastructure/Persistence/DbSchema.cs DevPulse.Infrastructure/Persistence/SqliteStateStore.cs
git commit -m "type-safe MuteEntry fields — Key was dual-purpose (PR id OR author key)"
```

---

## Task 9: `ParseStoredDate` — return `DateTimeOffset?` to surface corruption

**Files:**
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs`

- [ ] **Step 1: Change `ParseStoredDate` return type**

Replace the private method at line ~414:
```csharp
private static DateTimeOffset? ParseStoredDate(SqliteDataReader r, string column)
{
    var s = r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetString(r.GetOrdinal(column));
    if (s == null) return null;
    if (DateTimeOffset.TryParseExact(s, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        return dt;
    Log.Warning("SqliteStateStore: unparseable date in '{Column}': {Value}", column, s);
    return null;
}
```

- [ ] **Step 2: Update `ReadEvent` to handle nullable dates**

For `CreatedAtUtc` and `DiscoveredAtUtc` — these are required fields; fall back to `DateTimeOffset.MinValue` only when null and log:
```csharp
CreatedAtUtc = ParseStoredDate(r, "created_at_utc") ?? DateTimeOffset.MinValue,
DiscoveredAtUtc = ParseStoredDate(r, "discovered_at_utc") ?? DateTimeOffset.MinValue,
```

- [ ] **Step 3: Update `ReadWorkItem` to handle nullable dates**

```csharp
StateChangedAtUtc = ParseStoredDate(r, "state_changed_at") ?? DateTimeOffset.MinValue,
DiscoveredAtUtc = ParseStoredDate(r, "discovered_at") ?? DateTimeOffset.MinValue,
```

- [ ] **Step 4: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/Persistence/SqliteStateStore.cs
git commit -m "ParseStoredDate returns nullable — MinValue was silently masking DB corruption"
```

---

## Task 10: Toast notification — replace bare `catch {}` with specific exception logging

**Files:**
- Modify: `DevPulse.Infrastructure/Notifications/WindowsToastNotificationService.cs`

- [ ] **Step 1: Replace catch block**

```csharp
public Task ShowAsync(DevOpsEvent evt, CancellationToken ct = default)
{
    try
    {
        new ToastContentBuilder()
            .AddText(BuildTitle(evt))
            .AddText(BuildBody(evt))
            .Show();
    }
    catch (InvalidOperationException ex)
    {
        Log.Warning(ex, "Toast notification failed (notification platform unavailable)");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Toast notification failed unexpectedly for PR #{PrId}", evt.PullRequestId);
    }
    return Task.CompletedTask;
}
```

Add `using Serilog;` at the top if not already present.

- [ ] **Step 2: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 3: Commit**

```bash
git add DevPulse.Infrastructure/Notifications/WindowsToastNotificationService.cs
git commit -m "log toast notification failures instead of silently swallowing them"
```

---

## Task 11: HTTP client — classify 401/403/429 distinctly

**Files:**
- Modify: `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs`
- Modify: `DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs` (same pattern)

- [ ] **Step 1: Update `GetWithRetryAsync` in `AzureDevOpsClient.cs`**

Replace the retry helper to throw distinct exceptions for auth/rate-limit failures:
```csharp
private static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, CancellationToken ct)
{
    var delay = TimeSpan.FromSeconds(2);
    HttpResponseMessage? last = null;
    for (int attempt = 1; attempt <= 3; attempt++)
    {
        last = await http.GetAsync(url, ct);

        if (last.IsSuccessStatusCode) return last;

        var code = (int)last.StatusCode;
        if (code == 401) throw new HttpRequestException($"ADO GET unauthorized (401) — check PAT: {url}", null, last.StatusCode);
        if (code == 403) throw new HttpRequestException($"ADO GET forbidden (403) — missing permission: {url}", null, last.StatusCode);
        if (code == 429) throw new HttpRequestException($"ADO GET rate-limited (429): {url}", null, last.StatusCode);
        if (code < 500 || attempt == 3) break;

        await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
        delay *= 2;
    }
    throw new HttpRequestException($"ADO GET failed [{(int)last!.StatusCode}]: {url}", null, last.StatusCode);
}
```

Apply the same change to `PostWithRetryAsync`.

- [ ] **Step 2: Apply the same fix to `WorkItemClient.cs`** (identical retry methods)

- [ ] **Step 3: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 4: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs
git add DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs
git commit -m "classify HTTP 401/403/429 as distinct exceptions — generic HttpRequestException hid auth failures"
```

---

## Task 12: PAT auth — extract `AzureDevOpsAuthHandler` DelegatingHandler

**Files:**
- Create: `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsAuthHandler.cs`
- Modify: `DevPulse.App/TrayApplicationContext.cs` (`CreateHttpClient`)

- [ ] **Step 1: Create `AzureDevOpsAuthHandler.cs`**

```csharp
using System.Net.Http.Headers;

namespace DevPulse.Infrastructure.AzureDevOps;

/// <summary>
/// Adds Basic auth header only for requests to the configured ADO host.
/// Prevents PAT leakage to non-ADO endpoints (e.g., third-party links in PR descriptions).
/// </summary>
public sealed class AzureDevOpsAuthHandler : DelegatingHandler
{
    private readonly string _adoHost;
    private readonly AuthenticationHeaderValue _authHeader;

    public AzureDevOpsAuthHandler(string orgUrl, string pat)
        : base(new HttpClientHandler())
    {
        _adoHost = new Uri(orgUrl.TrimEnd('/')).Host;
        var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
        _authHeader = new AuthenticationHeaderValue("Basic", encoded);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.Host.Equals(_adoHost, StringComparison.OrdinalIgnoreCase) == true)
            request.Headers.Authorization = _authHeader;

        return base.SendAsync(request, ct);
    }
}
```

- [ ] **Step 2: Update `TrayApplicationContext.CreateHttpClient`**

Replace the method:
```csharp
private static System.Net.Http.HttpClient CreateHttpClient(string orgUrl, string pat)
{
    var client = new System.Net.Http.HttpClient(new AzureDevOpsAuthHandler(orgUrl, pat));
    client.Timeout = TimeSpan.FromSeconds(30);
    return client;
}
```

Update the call site in `InitializeAsync` (line ~54) to pass `orgUrl`:
```csharp
var httpClient = CreateHttpClient(appSettings.OrganizationUrl, patResult.Value!);
```

- [ ] **Step 3: Remove `DefaultRequestHeaders.Authorization` line from `CreateHttpClient`**

The old code set `client.DefaultRequestHeaders.Authorization = ...` — remove that. The handler now does this per-request.

- [ ] **Step 4: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/AzureDevOpsAuthHandler.cs
git add DevPulse.App/TrayApplicationContext.cs
git commit -m "scope PAT auth to ADO host via DelegatingHandler — DefaultRequestHeaders leaked credentials to all endpoints"
```

---

## Task 13: WorkItemClient — validate path inputs + enforce server-side `$top` limit

**Files:**
- Modify: `DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs`

- [ ] **Step 1: Add path-pattern validation to `GetWorkItemsAsync`**

Add a private validator at the top of `WorkItemClient`:
```csharp
private static readonly System.Text.RegularExpressions.Regex SafePathPattern =
    new(@"^[\w\s\\/\-\.]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

private static string ValidatePath(string value, string paramName)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new ArgumentException($"Path '{paramName}' cannot be empty.", paramName);
    if (!SafePathPattern.IsMatch(value))
        throw new ArgumentException($"Path '{paramName}' contains invalid characters: {value}", paramName);
    return value;
}
```

Update `GetIdsViaWiqlAsync` to validate before interpolating:
```csharp
private async Task<List<int>> GetIdsViaWiqlAsync(string areaPath, string? iterationPath, CancellationToken ct)
{
    var safeArea = ValidatePath(areaPath, nameof(areaPath));
    var safeIter = string.IsNullOrEmpty(iterationPath) ? null : ValidatePath(iterationPath, nameof(iterationPath));

    var wiql = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{WiqlLiteral(_project)}' " +
               $"AND [System.AreaPath] UNDER '{WiqlLiteral(safeArea)}' " +
               (safeIter == null ? "" : $"AND [System.IterationPath] UNDER '{WiqlLiteral(safeIter)}' ") +
               "AND [System.State] <> 'Removed' ORDER BY [System.ChangedDate] DESC";
    // ...
}
```

- [ ] **Step 2: Add `$top` limit to the WIQL query**

Append `&$top=500` to the WIQL URL to cap server-side results (500 is ADO's max for WIQL):
```csharp
var url = $"{_orgUrl}/{Uri.EscapeDataString(_project)}/_apis/wit/wiql?$top=500&api-version={ApiVersions.WorkItemQueryLanguage}";
```

- [ ] **Step 3: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 4: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs
git commit -m "validate area/iteration paths + enforce \$top=500 in WIQL — unsanitized paths could produce malformed queries"
```

---

## Task 14: `pr_snapshots` — add TTL cleanup

**Files:**
- Modify: `DevPulse.Infrastructure/Persistence/SqliteStateStore.cs` (add `CleanStaleSnapshotsAsync`)
- Modify: `DevPulse.Core/Interfaces/IStateStore.cs` (add interface method)
- Modify: `DevPulse.App/Services/PollingService.cs` (call cleanup at end of each successful poll)

- [ ] **Step 1: Add interface method**

In `IStateStore.cs`:
```csharp
Task CleanStaleSnapshotsAsync(int retainDays = 30, CancellationToken ct = default);
```

- [ ] **Step 2: Implement in `SqliteStateStore.cs`**

Add after `GetPrSnapshotAsync`:
```csharp
public async Task CleanStaleSnapshotsAsync(int retainDays = 30, CancellationToken ct = default)
{
    // Remove snapshots for PRs that have no events in the last retainDays.
    // This handles closed/abandoned PRs that will never be polled again.
    await _lock.WaitAsync(ct);
    try
    {
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM pr_snapshots
            WHERE pr_id NOT IN (
                SELECT DISTINCT pull_request_id FROM events
                WHERE discovered_at_utc >= @cutoff
            )
            """;
        cmd.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow.AddDays(-retainDays).ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
    finally { _lock.Release(); }
}
```

- [ ] **Step 3: Call from `PollingService.ExecutePollAsync`**

At the end of the successful poll (before `SetLastSuccessfulPollAsync`):
```csharp
await _store.CleanStaleSnapshotsAsync(30, ct);
```

- [ ] **Step 4: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Core/Interfaces/IStateStore.cs DevPulse.Infrastructure/Persistence/SqliteStateStore.cs
git add DevPulse.App/Services/PollingService.cs
git commit -m "add pr_snapshots TTL cleanup — no eviction path meant table grew unbounded"
```

---

## Task 15: `PollingService` — guard duplicate/missing reviewer Id in `ToDictionary`

**Files:**
- Modify: `DevPulse.App/Services/PollingService.cs` (line ~61 in `ExecutePollAsync`)

- [ ] **Step 1: Fix the dictionary construction**

Change line ~61:
```csharp
var currVotes = pr.Reviewers.ToDictionary(r => r.Id, r => r.Vote);
```
to:
```csharp
var currVotes = pr.Reviewers
    .Where(r => !string.IsNullOrEmpty(r.Id))
    .GroupBy(r => r.Id)
    .ToDictionary(g => g.Key, g => g.Last().Vote);
```

- [ ] **Step 2: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 3: Commit**

```bash
git add DevPulse.App/Services/PollingService.cs
git commit -m "guard against duplicate/empty reviewer Id in PollingService — ADO occasionally returns duplicates"
```

---

## Task 16: `PollingService` — guard null `UniqueName` in reviewer comparisons

**Files:**
- Modify: `DevPulse.App/Services/PollingService.cs` (lines ~116–117, ~207, ~233, ~257)

- [ ] **Step 1: Fix the `IsCurrentUserReviewer` comparisons**

There are three identical patterns:
```csharp
pr.Reviewers.Any(r => r.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase))
```

In each occurrence, change to:
```csharp
pr.Reviewers.Any(r => !string.IsNullOrEmpty(r.UniqueName) &&
    r.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase))
```

There is also one occurrence in `BuildReviewerAddedEvent` that checks just the specific reviewer:
```csharp
reviewer.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase)
```
Change to:
```csharp
!string.IsNullOrEmpty(reviewer.UniqueName) &&
reviewer.UniqueName.Equals(settings.CurrentUserCanonicalKey, StringComparison.OrdinalIgnoreCase)
```

- [ ] **Step 2: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 3: Commit**

```bash
git add DevPulse.App/Services/PollingService.cs
git commit -m "guard null UniqueName in reviewer comparisons — ADO returns empty string but defensive code needed"
```

---

## Task 17: `PollingLoopBase` — implement `IAsyncDisposable` to await loop task

**Files:**
- Modify: `DevPulse.App/Services/PollingLoopBase.cs`
- Modify: `DevPulse.App/TrayApplicationContext.cs` (use `await using` or call `DisposeAsync`)

- [ ] **Step 1: Update `PollingLoopBase` class declaration and `Dispose`**

```csharp
public abstract class PollingLoopBase : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private Task? _loopTask;

    // ... Start, RefreshNowAsync, RunLoopAsync, ExecuteSafeAsync unchanged ...

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        _runLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        // Synchronous dispose: cancel and wait briefly, then fall through.
        _cts.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _runLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
```

- [ ] **Step 2: Update `TrayApplicationContext.Dispose`**

In `TrayApplicationContext.cs`, the `_prPoller` and `_wiPoller` are still disposed synchronously in `Dispose(bool)`. Update to call `DisposeAsync().GetAwaiter().GetResult()` or, better, override `OnMainFormClosed` to trigger async cleanup. For now the safe minimal fix is:
```csharp
protected override void Dispose(bool disposing)
{
    if (disposing)
    {
        _trayIcon?.Dispose();
        _prPoller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _wiPoller?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    base.Dispose(disposing);
}
```

- [ ] **Step 3: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Services/PollingLoopBase.cs DevPulse.App/TrayApplicationContext.cs
git commit -m "PollingLoopBase: await _loopTask in DisposeAsync — synchronous Dispose had a shutdown race"
```

---

## Task 18: Standardize `CancellationToken` parameter naming to `ct`

The codebase uses `ct` consistently in implementations but some public interface members or inner methods use `cancellationToken`. A single scan-and-replace pass.

**Files:**
- Modify: any `.cs` file with `CancellationToken cancellationToken` in method signatures

- [ ] **Step 1: Find inconsistencies**

```
grep -rn "CancellationToken cancellationToken" --include="*.cs" .
```

- [ ] **Step 2: Replace each occurrence**

For each line found, rename the parameter from `cancellationToken` to `ct` in both the parameter declaration and all usages within that method.

- [ ] **Step 3: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 4: Commit**

```bash
git add -u
git commit -m "standardize CancellationToken parameter naming to 'ct' throughout"
```

---

## Task 19: WinForms async event handlers — add top-level `try/catch`

`async (_, _) => await SomeMethodAsync()` handlers crash the process on unhandled exceptions. All async event handlers need a top-level catch.

**Files:**
- Modify: `DevPulse.App/Forms/SettingsForm.cs`
- Modify: `DevPulse.App/Forms/BoardForm.cs`

- [ ] **Step 1: Fix `SettingsForm.cs` — Save button**

Change line ~76:
```csharp
btnSave.Click += async (_, _) =>
{
    try { await SaveSettingsAsync(); }
    catch (Exception ex)
    {
        Log.Error(ex, "SettingsForm: Save failed");
        MessageBox.Show($"Save failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
};
```

- [ ] **Step 2: Fix `SettingsForm.cs` — Test Connection button**

Change line ~100:
```csharp
btnTest.Click += async (_, _) =>
{
    try { await TestConnectionAsync(); }
    catch (Exception ex)
    {
        Log.Error(ex, "SettingsForm: TestConnection failed");
        MessageBox.Show($"Connection test failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
};
```

- [ ] **Step 3: Fix `BoardForm.cs` — Refresh button**

Change line ~109:
```csharp
btnRefresh.Click += async (_, _) =>
{
    try { await LoadAsync(); }
    catch (Exception ex) { Log.Error(ex, "BoardForm: Refresh failed"); }
};
```

- [ ] **Step 4: Add `using Serilog;` to both form files if not already present**

- [ ] **Step 5: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 6: Commit**

```bash
git add DevPulse.App/Forms/SettingsForm.cs DevPulse.App/Forms/BoardForm.cs
git commit -m "wrap async WinForms event handlers in try/catch — unhandled exceptions crash the process"
```

---

## Task 20: `SettingsForm` — fix `LoadInboxRules` fire-and-forget

**Files:**
- Modify: `DevPulse.App/Forms/SettingsForm.cs`

- [ ] **Step 1: Replace the fire-and-forget pattern in `LoadInboxRules`**

Change `LoadInboxRules` (line ~353):
```csharp
private void LoadInboxRules()
{
    var selectedName = _inboxList.SelectedItem?.ToString();
    if (selectedName == null) return;
    _ = LoadInboxRulesAsync(selectedName).ContinueWith(
        t => Log.Error(t.Exception?.GetBaseException(), "SettingsForm: LoadInboxRules failed"),
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
```

- [ ] **Step 2: Fix constructor fire-and-forget**

Change line ~38:
```csharp
_ = LoadSettingsAsync().ContinueWith(
    t => Log.Error(t.Exception?.GetBaseException(), "SettingsForm: LoadSettings failed"),
    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
```

- [ ] **Step 3: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Forms/SettingsForm.cs
git commit -m "log SettingsForm fire-and-forget task failures — exceptions were silently swallowed"
```

---

## Task 21: `SettingsForm.SaveSettingsAsync` — add required-field validation

**Files:**
- Modify: `DevPulse.App/Forms/SettingsForm.cs`

- [ ] **Step 1: Add validation at the start of `SaveSettingsAsync`**

Insert before the first assignment (line ~307):
```csharp
private async Task SaveSettingsAsync()
{
    // Validate required fields
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(_orgUrl.Text))
        errors.Add("Organization URL is required.");
    if (string.IsNullOrWhiteSpace(_project.Text))
        errors.Add("Project name is required.");
    if (string.IsNullOrWhiteSpace(_currentUser.Text))
        errors.Add("Your email (canonical key) is required.");
    if (errors.Count > 0)
    {
        MessageBox.Show(string.Join("\n", errors), "DevPulse — Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
    }

    // ... rest of SaveSettingsAsync unchanged ...
```

- [ ] **Step 2: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 3: Commit**

```bash
git add DevPulse.App/Forms/SettingsForm.cs
git commit -m "validate required fields in SettingsForm before saving — silent saves with blank org/project caused cryptic polling failures"
```

---

## Task 22: `BoardForm.RenderBoard` — use `filteredItems` for grouping

**Files:**
- Modify: `DevPulse.App/Forms/BoardForm.cs` (line ~182)

- [ ] **Step 1: Change the `GroupByColumn` call**

In `RenderBoard`, line ~182, change:
```csharp
var grouped = _boardService.GroupByColumn(_allItems, _columns);
```
to:
```csharp
var grouped = _boardService.GroupByColumn(filteredItems, _columns);
```

This ensures column counts reflect the active filters, not the full unfiltered dataset.

- [ ] **Step 2: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 3: Commit**

```bash
git add DevPulse.App/Forms/BoardForm.cs
git commit -m "fix BoardForm column count — RenderBoard was grouping _allItems instead of the filtered list"
```

---

## Task 23: `AzureDevOpsClient` — paginate PR list

**Files:**
- Modify: `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs`

- [ ] **Step 1: Replace the PR fetch with a paginating loop**

Replace `GetRelevantPullRequestsAsync`:
```csharp
public async Task<IReadOnlyList<PullRequestDto>> GetRelevantPullRequestsAsync(CancellationToken ct = default)
{
    const int pageSize = 200;
    const int maxPages = 5;  // cap at 1000 PRs to prevent runaway fetches
    var allPrs = new List<PullRequestDto>();

    for (int page = 0; page < maxPages; page++)
    {
        var skip = page * pageSize;
        var url = $"{_orgUrl}/{Uri.EscapeDataString(_project)}/_apis/git/pullrequests" +
                  $"?searchCriteria.status=all&$top={pageSize}&$skip={skip}&api-version={ApiVersions.PullRequests}";

        if (!string.IsNullOrWhiteSpace(_repoFilter))
            url += $"&searchCriteria.repositoryId={Uri.EscapeDataString(_repoFilter)}";

        var response = await GetWithRetryAsync(_http, url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<AdoListResponse<AdoPullRequest>>(body, JsonOpts);
        var page_items = result?.Value;
        if (page_items == null || page_items.Count == 0) break;

        allPrs.AddRange(page_items.Select(Map));
        if (page_items.Count < pageSize) break;  // last page
    }

    return allPrs;
}
```

- [ ] **Step 2: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 3: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs
git commit -m "paginate PR list up to 1000 results — \$top=200 silently dropped PRs in large orgs"
```

---

## Task 24: Tray icon — skip menu rebuild when counts unchanged

**Files:**
- Modify: `DevPulse.App/TrayApplicationContext.cs`

- [ ] **Step 1: Add a count cache field**

Add to the class fields:
```csharp
private Dictionary<string, int> _lastMenuCounts = [];
```

- [ ] **Step 2: Refactor `RefreshTrayAsync` and `RebuildMenuAsync` to share data**

Replace both methods:
```csharp
private async Task RefreshTrayAsync()
{
    var inboxes = await _settings.GetInboxDefinitionsAsync();
    var appSettings = await _settings.GetAppSettingsAsync();
    var counts = new Dictionary<string, int>();
    foreach (var inbox in inboxes)
        counts[inbox.Name] = await _store.GetUnreadCountForInboxAsync(inbox.Name);

    // Only rebuild the context menu when counts actually changed
    bool changed = counts.Count != _lastMenuCounts.Count ||
                   counts.Any(kv => !_lastMenuCounts.TryGetValue(kv.Key, out var prev) || prev != kv.Value);
    if (changed)
    {
        _lastMenuCounts = counts;
        RebuildMenu(inboxes, counts, appSettings);
    }

    var nma = counts.GetValueOrDefault("Needs My Attention");
    var text = nma > 0 ? $"DevPulse — Needs My Attention: {nma}" : "DevPulse — No attention needed";
    if (_trayIcon != null)
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
}

private void RebuildMenu(
    IReadOnlyList<InboxDefinition> inboxes,
    Dictionary<string, int> counts,
    AppSettings appSettings)
{
    var builder = new TrayMenuBuilder();
    var menu = builder.Build(
        inboxes, counts,
        refreshPrs: () => RunBackground(() => _prPoller?.RefreshNowAsync() ?? Task.CompletedTask, "refresh-prs"),
        refreshBoard: () => RunBackground(() => _wiPoller?.RefreshNowAsync() ?? Task.CompletedTask, "refresh-board"),
        openInbox: name => ShowInbox(name),
        openBoard: ShowBoard,
        openMuted: ShowMuted,
        openSettings: ShowSettings,
        openDebug: ShowDebug,
        orgUrl: appSettings.OrganizationUrl,
        exit: () => Application.Exit());

    if (_trayIcon != null)
    {
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.ContextMenuStrip = menu;
    }
}
```

Remove the old `RebuildMenuAsync` and the `BuildTrayIcon` call to `RunBackground(RebuildMenuAsync, ...)`. Update `BuildTrayIcon` to not call `RebuildMenuAsync` (the first `RefreshTrayAsync` will handle it).

- [ ] **Step 3: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/TrayApplicationContext.cs
git commit -m "skip tray menu rebuild when inbox counts unchanged — was disposing/recreating ContextMenuStrip on every poll tick"
```

---

## Task 25: Extract shared `AdoRetryHelper` — remove duplicated retry logic

**Files:**
- Create: `DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs`
- Modify: `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs`
- Modify: `DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs`

- [ ] **Step 1: Create `AdoRetryHelper.cs`**

```csharp
namespace DevPulse.Infrastructure.AzureDevOps;

internal static class AdoRetryHelper
{
    internal static async Task<HttpResponseMessage> GetWithRetryAsync(HttpClient http, string url, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        HttpResponseMessage? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            last = await http.GetAsync(url, ct);
            if (last.IsSuccessStatusCode) return last;

            var code = (int)last.StatusCode;
            if (code == 401) throw new HttpRequestException($"ADO GET unauthorized (401) — check PAT: {url}", null, last.StatusCode);
            if (code == 403) throw new HttpRequestException($"ADO GET forbidden (403) — missing permission: {url}", null, last.StatusCode);
            if (code == 429) throw new HttpRequestException($"ADO GET rate-limited (429): {url}", null, last.StatusCode);
            if (code < 500 || attempt == 3) break;

            await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
            delay *= 2;
        }
        throw new HttpRequestException($"ADO GET failed [{(int)last!.StatusCode}]: {url}", null, last.StatusCode);
    }

    internal static async Task<HttpResponseMessage> PostWithRetryAsync(HttpClient http, string url, HttpContent content, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(2);
        HttpResponseMessage? last = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            last = await http.PostAsync(url, content, ct);
            if (last.IsSuccessStatusCode) return last;

            var code = (int)last.StatusCode;
            if (code == 401) throw new HttpRequestException($"ADO POST unauthorized (401) — check PAT: {url}", null, last.StatusCode);
            if (code == 403) throw new HttpRequestException($"ADO POST forbidden (403) — missing permission: {url}", null, last.StatusCode);
            if (code == 429) throw new HttpRequestException($"ADO POST rate-limited (429): {url}", null, last.StatusCode);
            if (code < 500 || attempt == 3) break;

            await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
            delay *= 2;
        }
        throw new HttpRequestException($"ADO POST failed [{(int)last!.StatusCode}]: {url}", null, last.StatusCode);
    }
}
```

- [ ] **Step 2: Replace private retry methods in `AzureDevOpsClient.cs`**

Delete the two private static `GetWithRetryAsync` and `PostWithRetryAsync` methods. Replace all call sites:
```csharp
// Before:
var response = await GetWithRetryAsync(_http, url, ct);
// After:
var response = await AdoRetryHelper.GetWithRetryAsync(_http, url, ct);
```

- [ ] **Step 3: Same replacement in `WorkItemClient.cs`**

Delete the two private static methods. Replace all call sites with `AdoRetryHelper.GetWithRetryAsync` and `AdoRetryHelper.PostWithRetryAsync`.

- [ ] **Step 4: Build clean**

```
dotnet build DevPulse.sln
```

- [ ] **Step 5: Run all tests**

```
dotnet test DevPulse.Tests/DevPulse.Tests.csproj -v minimal
```
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs
git add DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs
git add DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs
git commit -m "extract AdoRetryHelper — retry logic was duplicated verbatim in both ADO clients"
```

---

## Self-Review Checklist

**Spec coverage** (all 29 findings):

| # | Finding | Task |
|---|---------|------|
| 1 | PollingService null reviewer.UniqueName | Task 16 |
| 2 | IdentityNormalizer null DisplayName | Task 6 |
| 3 | RuleEngine empty keyword wildcard | Task 5 |
| 4 | PollingService ToDictionary duplicate Id | Task 15 |
| 5 | SettingsForm fire-and-forget | Task 20 |
| 6 | PollingLoopBase Dispose race | Task 17 |
| 7 | SettingsForm no validation | Task 21 |
| 8 | WorkItemNormalizer negative days | Task 4 |
| 9 | Toast bare catch | Task 10 |
| 10 | HTTP 401/403/429 not classified | Task 11 |
| 11 | ParseStoredDate MinValue masks corruption | Task 9 |
| 12 | MuteEntry.Key dual-purpose | Task 8 |
| 13 | WatcherType ambiguous naming | Task 2 |
| 14 | TrayApplicationContext god object | ⚠ SEPARATE PLAN |
| 15 | Inbox stable IDs | ⚠ SEPARATE PLAN |
| 16 | pr_snapshots no cleanup | Task 14 |
| 17 | IStateStore split | ⚠ SEPARATE PLAN |
| 18 | PR vs WI client contract inconsistency | Task 25 |
| 19 | BoardForm RenderBoard wrong dataset | Task 22 |
| 20 | Full menu rebuild on every refresh | Task 24 |
| 21 | PAT in DefaultRequestHeaders | Task 12 |
| 22 | WIQL still abusable | Task 13 |
| 23 | EventSource collides with .NET type | Task 3 |
| 24 | AgingLevel.Aging misleading | Task 1 |
| 25 | CancellationToken naming inconsistent | Task 18 |
| 26 | IsCollapsed + CollapsedCount drift | Task 7 |
| 27 | No pagination for PR list | Task 23 |
| 28 | No schema migration runner | ⚠ SEPARATE PLAN |
| 29 | async void handlers no try/catch | Task 19 |

**Placeholder scan:** No TBDs, no "fill in later" sections. All code blocks are complete.

**Type consistency:**
- `PrEventSource` introduced in Task 3, used by name in Tasks 4, 6 tests — consistent.
- `AgingLevel.Warning` introduced in Task 1, used in Task 4 test — consistent.
- `MuteEntry.PrId/AuthorKey` introduced in Task 8, used by `MuteService` in same task — consistent.
- `AdoRetryHelper` introduced in Task 25, replaces identical inline code in Tasks 11/12 — consistent.

**Dependency order check:**
- Tasks 1–3 (enum renames) have no deps → correct to do first.
- Tasks 4–6 (Core logic) depend on correct enum names from Tasks 1–3 → correct ordering.
- Task 7 (IsCollapsed) modifies DevOpsEvent; SqliteStateStore reads depend on it → Task 9 comes after → correct.
- Task 8 (MuteEntry) modifies model + DB; Task 9 modifies ParseStoredDate in same file → they're independent of each other → fine.
- Task 12 (DelegatingHandler) introduces `AzureDevOpsAuthHandler`; Task 25 extracts retry into `AdoRetryHelper`; both modify `AzureDevOpsClient` → if done in the order listed (12 then 25), no conflict.
- Tasks 15–16 both modify `PollingService.cs` → implementer should do both before committing if possible, or commit each as a separate small change.
