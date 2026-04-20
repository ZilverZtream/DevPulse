# DevPulse Audit Fixes — Pass 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the 9 in-scope actionable bugs from the Pass 2 audit; architectural findings (IStateStore split, inbox stable IDs, schema migration, DaysInCurrentState staleness, client contract standardization) are explicitly deferred to a separate plan.

**Architecture:** Fixes are layered: Infrastructure first (AdoRetryHelper, WorkItemClient, AzureDevOpsClient), then App services (PollingLoopBase, PollingService), then UI forms (SettingsForm ×2, BoardForm, InboxEventsForm). Each task is independent of the others except that Task 1 modifies `AdoRetryHelper.cs`, which Tasks 2 and 3 indirectly depend on only by being in the same Infrastructure layer — no runtime coupling.

**Tech Stack:** .NET 8, C# 12, WinForms, xUnit + FluentAssertions, Serilog, Microsoft.Data.Sqlite

---

## Out-of-scope (deferred — need separate brainstorm/plan)

- **PASS2-CATEGORY6**: IStateStore split into IEventStore/IWorkItemStore/IMuteStore/IPollStateStore
- **PASS2-CATEGORY7**: Inbox name as unstable FK; DaysInCurrentState stored vs derived
- **PASS2-CATEGORY8**: Client contract standardization (IAzureDevOpsClient vs IWorkItemClient)
- **PASS3-CATEGORY14**: Schema version migration runner

---

## Files changed

| File | Task | Change |
|------|------|--------|
| `DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs` | 1 | Strip query strings from exception URLs |
| `DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs` | 2 | Replace over-restrictive regex with control-char denylist |
| `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs` | 3 | Log warning when PR hard cap is hit |
| `DevPulse.App/Services/PollingLoopBase.cs` | 4 | Interlocked guard prevents duplicate Start() loops |
| `DevPulse.App/Services/PollingService.cs` | 5 | Null-safe comment.Author + skip empty reviewer.Id in vote loop |
| `DevPulse.App/Forms/SettingsForm.cs` | 6, 7 | PAT test uses AzureDevOpsAuthHandler; NumericUpDown clamped |
| `DevPulse.App/Forms/BoardForm.cs` | 8 | Fix BeginInvoke async-recursion pattern |
| `DevPulse.App/Forms/InboxEventsForm.cs` | 9 | Add try/catch to all async UI event handlers |
| `DevPulse.Tests/CoreLogicTests.cs` | 2, 4 | Tests for WIQL validation and Start() guard |

---

## Task 1: Strip query strings from AdoRetryHelper exception messages

**Audit ref:** PASS1-CATEGORY4 CRITICAL — retry helper leaks full URL (project/repo identifiers in query params) into exception messages that surface in logs and debug UI.

**Files:**
- Modify: `DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs`

- [ ] **Step 1: Read the current file**

Open `DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs`. Note the 6 `throw new HttpRequestException(...)` calls — each embeds `{url}` directly. The URLs contain `$skip`, `$top`, `repositoryId`, `ids=`, etc. in query strings.

- [ ] **Step 2: Add `SafeEndpoint` helper and replace all URL references in messages**

Replace the entire file content:

```csharp
using Serilog;

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
            var ep = SafeEndpoint(url);
            if (code == 401) throw new HttpRequestException($"ADO GET unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
            if (code == 403) throw new HttpRequestException($"ADO GET forbidden (403) — missing permission: {ep}", null, last.StatusCode);
            if (code == 429) throw new HttpRequestException($"ADO GET rate-limited (429): {ep}", null, last.StatusCode);
            if (code < 500 || attempt == 3) break;

            await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
            delay *= 2;
        }
        var finalEp = SafeEndpoint(url);
        throw new HttpRequestException($"ADO GET failed [{(int)last!.StatusCode}]: {finalEp}", null, last.StatusCode);
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
            var ep = SafeEndpoint(url);
            if (code == 401) throw new HttpRequestException($"ADO POST unauthorized (401) — check PAT: {ep}", null, last.StatusCode);
            if (code == 403) throw new HttpRequestException($"ADO POST forbidden (403) — missing permission: {ep}", null, last.StatusCode);
            if (code == 429) throw new HttpRequestException($"ADO POST rate-limited (429): {ep}", null, last.StatusCode);
            if (code < 500 || attempt == 3) break;

            await Task.Delay(delay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)), ct);
            delay *= 2;
        }
        var finalEp = SafeEndpoint(url);
        throw new HttpRequestException($"ADO POST failed [{(int)last!.StatusCode}]: {finalEp}", null, last.StatusCode);
    }

    internal static string SafeEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "(invalid url)";
}
```

- [ ] **Step 3: Build**

```bash
dotnet build DevPulse.Infrastructure -v q
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/AdoRetryHelper.cs
git commit -m "fix: strip query strings from AdoRetryHelper exception messages"
```

---

## Task 2: Remove over-restrictive WIQL path validation regex

**Audit ref:** PASS1-CATEGORY3 MAJOR — `SafePathPattern` regex `^[\w\s\\/\-\.]+$` rejects legitimate ADO area/iteration path characters like `&`, `(`, `)`, `:`, which appear in real enterprise naming conventions.

**Files:**
- Modify: `DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs:24-34`
- Modify: `DevPulse.Tests/CoreLogicTests.cs`

The WIQL query already calls `WiqlLiteral()` to escape single quotes (the only WIQL injection risk). Blocking control characters (< 32) and semicolons is a sufficient safety net.

- [ ] **Step 1: Write a failing test**

Add to `DevPulse.Tests/CoreLogicTests.cs`:

```csharp
// Note: WorkItemClient.ValidatePath is private. We test it indirectly by
// verifying GetWorkItemsAsync does NOT throw for valid ADO names with special chars.
// Since WorkItemClient requires a real HTTP call, we test the validation logic
// by extracting it to an internal helper and calling it directly.
//
// For this task, the test is a compile-time proof: update ValidatePath to be
// internal (not private), add InternalsVisibleTo, and test it directly.
//
// Simpler approach: test that a path with & and : does NOT throw ArgumentException.
// We'll add this as a separate public static helper in WorkItemClient.

public class WiqlPathValidationTests
{
    [Theory]
    [InlineData(@"MyOrg\MyProject\Team & QA")]
    [InlineData(@"Sprint (2024-Q1)")]
    [InlineData("Area: Platform")]
    [InlineData(@"Backlog\Feature: Auth")]
    public void ValidatePath_LegitimateAdoNames_DoesNotThrow(string path)
    {
        // ValidateWiqlPath is a new internal-visible static for testability
        var result = WorkItemClient.ValidateWiqlPath(path, "areaPath");
        Assert.Equal(path, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("path;DROP TABLE--")]
    [InlineData("path\x00null")]
    [InlineData("path\ninjection")]
    public void ValidatePath_InvalidPaths_Throws(string path)
    {
        Assert.Throws<ArgumentException>(() => WorkItemClient.ValidateWiqlPath(path, "areaPath"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails (won't compile yet)**

```bash
dotnet build DevPulse.Tests -v q
```

Expected: FAIL — `WorkItemClient.ValidateWiqlPath` does not exist yet.

- [ ] **Step 3: Update `WorkItemClient.cs`**

Replace lines 24–34 (the `SafePathPattern` field and `ValidatePath` method) with:

```csharp
    public static string ValidateWiqlPath(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Path '{paramName}' cannot be empty.", paramName);
        if (value.Any(c => c < 32 || c == ';'))
            throw new ArgumentException($"Path '{paramName}' contains invalid characters.", paramName);
        return value;
    }
```

Also update the private call sites inside `GetIdsViaWiqlAsync` (lines 54–55) to use the new name:

```csharp
        var safeArea = ValidateWiqlPath(areaPath, nameof(areaPath));
        var safeIter = string.IsNullOrEmpty(iterationPath) ? null : ValidateWiqlPath(iterationPath, nameof(iterationPath));
```

Remove the now-unused `using System.Text.RegularExpressions;` import if present (it was inline-qualified, so check the top of the file — the `Regex` and `RegexOptions` types were fully qualified in the field declaration, so no using import was added).

- [ ] **Step 4: Run tests**

```bash
dotnet test DevPulse.Tests -v q
```

Expected: all tests PASS including the 2 new `WiqlPathValidationTests` tests.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/WorkItemClient.cs DevPulse.Tests/CoreLogicTests.cs
git commit -m "fix: replace over-restrictive WIQL path regex with control-char denylist"
```

---

## Task 3: Warn when PR pagination hard cap is hit

**Audit ref:** PASS2-CATEGORY9 MAJOR — pagination loop exits silently when it reaches 5 pages × 200 = 1000 PRs; users with larger orgs miss data with no indication.

**Files:**
- Modify: `DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs:36-56`

- [ ] **Step 1: Update `GetRelevantPullRequestsAsync`**

Replace the for-loop body and return (lines 36–56):

```csharp
    public async Task<IReadOnlyList<PullRequestDto>> GetRelevantPullRequestsAsync(CancellationToken ct = default)
    {
        const int pageSize = 200;
        const int maxPages = 5;
        var allPrs = new List<PullRequestDto>();
        bool reachedHardCap = true;

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
            if (pageItems == null || pageItems.Count == 0) { reachedHardCap = false; break; }

            allPrs.AddRange(pageItems.Select(Map));
            if (pageItems.Count < pageSize) { reachedHardCap = false; break; }
        }

        if (reachedHardCap)
            Log.Warning("PR fetch reached the {Cap}-item hard cap; some PRs may have been skipped. Configure a repository filter to reduce scope.", allPrs.Count);

        return allPrs;
    }
```

Add `using Serilog;` at the top of `AzureDevOpsClient.cs` if not already present.

- [ ] **Step 2: Build**

```bash
dotnet build DevPulse.Infrastructure -v q
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add DevPulse.Infrastructure/AzureDevOps/AzureDevOpsClient.cs
git commit -m "fix: warn when PR pagination reaches the 1000-item hard cap"
```

---

## Task 4: Guard PollingLoopBase.Start() against duplicate calls

**Audit ref:** PASS1-CATEGORY2 CRITICAL — `Start()` blindly overwrites `_loopTask` without checking if a loop is already running; calling `Start()` twice creates two concurrent polling loops, duplicating API calls and racing on SQLite writes.

**Files:**
- Modify: `DevPulse.App/Services/PollingLoopBase.cs:6-22`
- Modify: `DevPulse.Tests/CoreLogicTests.cs`

- [ ] **Step 1: Write a failing test**

Add to `DevPulse.Tests/CoreLogicTests.cs`:

```csharp
public class PollingLoopBaseTests
{
    private sealed class CountingPoller : PollingLoopBase
    {
        public int InitialPollCount;
        protected override string TrackName => "test";
        protected override Task ExecutePollAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref InitialPollCount);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Start_CalledTwice_OnlyOneInitialPollFires()
    {
        using var poller = new CountingPoller();
        poller.Start(60); // 60-min interval — only initial poll fires before we check
        poller.Start(60); // second call must be a no-op
        await Task.Delay(80); // give the initial poll time to complete
        Assert.Equal(1, poller.InitialPollCount);
    }
}
```

Note: `CountingPoller` must be in `DevPulse.Tests` which already references `DevPulse.App`. Add `using DevPulse.App.Services;` to the test file.

- [ ] **Step 2: Run test to verify it fails**

```bash
dotnet test DevPulse.Tests --filter "PollingLoopBaseTests" -v n
```

Expected: FAIL — `InitialPollCount` is 2 (both `Start()` calls fire an initial poll).

- [ ] **Step 3: Add `_started` field and guard to `PollingLoopBase`**

In `DevPulse.App/Services/PollingLoopBase.cs`, add the field after the existing fields and update `Start()`:

```csharp
public abstract class PollingLoopBase : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private Task? _loopTask;
    private int _started;

    public event EventHandler? PollCompleted;
    public bool LastPollFailed { get; private set; }

    protected abstract string TrackName { get; }
    protected abstract Task ExecutePollAsync(CancellationToken ct);
    protected virtual Task OnPollFailedAsync(Exception ex, CancellationToken ct) => Task.CompletedTask;

    public void Start(int intervalMinutes)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        var clamped = Math.Clamp(intervalMinutes, 1, 1440);
        _loopTask = RunLoopAsync(TimeSpan.FromMinutes(clamped), _cts.Token);
    }

    // ... rest unchanged
```

- [ ] **Step 4: Run tests**

```bash
dotnet test DevPulse.Tests -v q
```

Expected: all tests PASS including the new `PollingLoopBaseTests.Start_CalledTwice_OnlyOneInitialPollFires`.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.App/Services/PollingLoopBase.cs DevPulse.Tests/CoreLogicTests.cs
git commit -m "fix: prevent duplicate polling loops from double Start() call"
```

---

## Task 5: Guard null Author access and empty reviewer.Id in PollingService

**Audit ref:** PASS1-CATEGORY1 CRITICAL — `comment.Author.DisplayName` at line 145 is a direct access that becomes fragile if `MapIdentity`'s null-protection is ever relaxed; empty reviewer.Id in the vote-diff loop creates false "new reviewer" events.

**Files:**
- Modify: `DevPulse.App/Services/PollingService.cs:76-82, 145`

No new tests needed — these are inline guards in a complex polling method that requires a mock ADO server to unit-test end-to-end. The changes are defensive.

- [ ] **Step 1: Fix `comment.Author.DisplayName` (line 145)**

In `ExecutePollAsync`, find the `allNewEvents.Add(new DevOpsEvent { ... })` block inside the comment loop. Change:

```csharp
                        AuthorDisplayName = comment.Author.DisplayName,
```

to:

```csharp
                        AuthorDisplayName = comment.Author?.DisplayName ?? string.Empty,
```

- [ ] **Step 2: Fix the reviewer vote-diff loop (lines 76–82)**

Find the `foreach (var reviewer in pr.Reviewers)` inside the `if (prevVotesJson != null)` block. Add an early-exit guard as the first line of the loop body:

```csharp
            if (prevVotesJson != null)
            {
                var prevVotes = JsonSerializer.Deserialize<Dictionary<string, int>>(prevVotesJson) ?? [];
                foreach (var reviewer in pr.Reviewers)
                {
                    if (string.IsNullOrEmpty(reviewer.Id)) continue;
                    if (prevVotes.TryGetValue(reviewer.Id, out var prevVote) && prevVote != reviewer.Vote)
                        allNewEvents.Add(BuildVoteEvent(pr, reviewer, appSettings, idNorm, pollTime));
                    else if (!prevVotes.ContainsKey(reviewer.Id))
                        allNewEvents.Add(BuildReviewerAddedEvent(pr, reviewer, appSettings, idNorm, pollTime));
                }
            }
```

- [ ] **Step 3: Build**

```bash
dotnet build DevPulse.App -v q
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Services/PollingService.cs
git commit -m "fix: null-safe comment author access and skip empty reviewer.Id in vote loop"
```

---

## Task 6: SettingsForm PAT test — use AzureDevOpsAuthHandler

**Audit ref:** PASS1-CATEGORY10 CRITICAL — `TestConnectionAsync` sets PAT as `DefaultRequestHeaders.Authorization` on a bare `HttpClient`; any additional requests on that same client instance would carry the PAT to non-ADO endpoints.

**Files:**
- Modify: `DevPulse.App/Forms/SettingsForm.cs:1-4, 407-426`

- [ ] **Step 1: Add using directive**

At the top of `SettingsForm.cs`, add:

```csharp
using DevPulse.App.Services;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.AzureDevOps;
using DevPulse.Infrastructure.Security;
```

(Add `using DevPulse.Infrastructure.AzureDevOps;` — the other three already exist.)

- [ ] **Step 2: Replace `TestConnectionAsync`**

Replace the entire method:

```csharp
    private async Task TestConnectionAsync()
    {
        try
        {
            var orgUrl = _orgUrl.Text.TrimEnd('/');
            var pat = _patBox.Text;
            using var handler = new AzureDevOpsAuthHandler(orgUrl, pat);
            using var http = new System.Net.Http.HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            var resp = await http.GetAsync($"{orgUrl}/_apis/projects?api-version=7.1");
            if (resp.IsSuccessStatusCode)
                MessageBox.Show("Connection successful!", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show($"Connection failed: {resp.StatusCode}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
```

- [ ] **Step 3: Build**

```bash
dotnet build DevPulse.App -v q
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Forms/SettingsForm.cs
git commit -m "fix: use AzureDevOpsAuthHandler for PAT test connection"
```

---

## Task 7: Clamp NumericUpDown values before assignment in SettingsForm

**Audit ref:** PASS1-CATEGORY3 CRITICAL — assigning an out-of-range DB value directly to `NumericUpDown.Value` throws `ArgumentOutOfRangeException`, preventing the Settings window from opening.

**Files:**
- Modify: `DevPulse.App/Forms/SettingsForm.cs:300-301, 307`

- [ ] **Step 1: Replace the three bare assignments in `LoadSettingsAsync`**

Find (lines 300–301, 307):

```csharp
        _prInterval.Value = _appSettings.PrPollingIntervalMinutes;
        _wiInterval.Value = _appSettings.WorkItemPollingIntervalMinutes;
```

and:

```csharp
        _maxEvents.Value = _appSettings.MaxEventsPerInbox;
```

Replace with:

```csharp
        _prInterval.Value = Math.Clamp(_appSettings.PrPollingIntervalMinutes, (int)_prInterval.Minimum, (int)_prInterval.Maximum);
        _wiInterval.Value = Math.Clamp(_appSettings.WorkItemPollingIntervalMinutes, (int)_wiInterval.Minimum, (int)_wiInterval.Maximum);
```

and:

```csharp
        _maxEvents.Value = Math.Clamp(_appSettings.MaxEventsPerInbox, (int)_maxEvents.Minimum, (int)_maxEvents.Maximum);
```

- [ ] **Step 2: Build**

```bash
dotnet build DevPulse.App -v q
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add DevPulse.App/Forms/SettingsForm.cs
git commit -m "fix: clamp NumericUpDown values to prevent ArgumentOutOfRangeException on load"
```

---

## Task 8: Fix BoardForm.LoadAsync BeginInvoke async-recursion pattern

**Audit ref:** PASS1-CATEGORY2 MAJOR — `BeginInvoke(LoadAsync)` when `InvokeRequired` schedules a new async Task on the UI thread with no awaiter; exceptions are unobserved and the async data loads happen twice (once off-thread, once again on-thread).

**Files:**
- Modify: `DevPulse.App/Forms/BoardForm.cs:125-151`

The fix: do all async I/O first (which is thread-safe), then marshal only the synchronous UI-mutation into `Invoke()`.

- [ ] **Step 1: Read the current `LoadAsync` method**

The current method (approximately lines 125–151 of `BoardForm.cs`):

```csharp
    public async Task LoadAsync()
    {
        _allItems = await _store.GetWorkItemsAsync();
        _columns = await _settings.GetBoardColumnsAsync();
        _appSettings = await _settings.GetAppSettingsAsync();

        // Populate assignee dropdown
        var assignees = _allItems
            .Where(i => !string.IsNullOrEmpty(i.AssignedToDisplayName))
            .Select(i => i.AssignedToDisplayName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (InvokeRequired) { BeginInvoke(LoadAsync); return; }

        _assigneeFilter.Items.Clear();
        _assigneeFilter.Items.Add("All assignees");
        foreach (var a in assignees) _assigneeFilter.Items.Add(a);
        if (_assigneeFilter.SelectedIndex < 0) _assigneeFilter.SelectedIndex = 0;

        ApplyFilters();
    }
```

- [ ] **Step 2: Replace with the corrected version**

```csharp
    public async Task LoadAsync()
    {
        _allItems = await _store.GetWorkItemsAsync();
        _columns = await _settings.GetBoardColumnsAsync();
        _appSettings = await _settings.GetAppSettingsAsync();

        var assignees = _allItems
            .Where(i => !string.IsNullOrEmpty(i.AssignedToDisplayName))
            .Select(i => i.AssignedToDisplayName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        void ApplyUi()
        {
            _assigneeFilter.Items.Clear();
            _assigneeFilter.Items.Add("All assignees");
            foreach (var a in assignees) _assigneeFilter.Items.Add(a);
            if (_assigneeFilter.SelectedIndex < 0) _assigneeFilter.SelectedIndex = 0;
            ApplyFilters();
        }

        if (InvokeRequired) Invoke(ApplyUi);
        else ApplyUi();
    }
```

Key differences:
- `Invoke` (synchronous) instead of `BeginInvoke` (fire-and-forget) — exceptions propagate to the caller's task.
- Data loads happen exactly once regardless of which thread calls `LoadAsync`.
- The `InvokeRequired` check is at the end, after all async work is done.

- [ ] **Step 3: Build**

```bash
dotnet build DevPulse.App -v q
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DevPulse.App/Forms/BoardForm.cs
git commit -m "fix: BoardForm.LoadAsync — marshal only UI update via Invoke, not full async re-entry"
```

---

## Task 9: Add try/catch to InboxEventsForm async event handlers

**Audit ref:** PASS3-CATEGORY15 MAJOR — `btnMarkAll.Click`, `btnRefresh.Click`, and the four context-menu async handlers fire async lambdas with no exception boundary; unhandled exceptions from DB or service calls crash the process.

**Files:**
- Modify: `DevPulse.App/Forms/InboxEventsForm.cs:1-5, 51, 54, 128-131`

- [ ] **Step 1: Add Serilog using**

At the top of `InboxEventsForm.cs`, add `using Serilog;`:

```csharp
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using Serilog;
```

- [ ] **Step 2: Wrap toolbar button handlers**

Find (lines 51–54):

```csharp
        var btnMarkAll = DarkButton("Mark all as read");
        btnMarkAll.Click += async (_, _) => await MarkAllReadAsync();

        var btnRefresh = DarkButton("Refresh");
        btnRefresh.Click += async (_, _) => await LoadEventsAsync();
```

Replace with:

```csharp
        var btnMarkAll = DarkButton("Mark all as read");
        btnMarkAll.Click += async (_, _) =>
        {
            try { await MarkAllReadAsync(); }
            catch (Exception ex) { Log.Error(ex, "Mark all read failed"); }
        };

        var btnRefresh = DarkButton("Refresh");
        btnRefresh.Click += async (_, _) =>
        {
            try { await LoadEventsAsync(); }
            catch (Exception ex) { Log.Error(ex, "Inbox refresh failed"); }
        };
```

- [ ] **Step 3: Wrap context-menu handlers**

Find (lines 128–131):

```csharp
        menu.Items.Add("Mark as read", null, async (_, _) => { await _viewService.MarkReadAsync([evt.EventId]); await LoadEventsAsync(); });
        menu.Items.Add("Snooze PR (1h)", null, async (_, _) => await SnoozePrAsync(evt.PullRequestId, TimeSpan.FromHours(1)));
        menu.Items.Add("Snooze PR (4h)", null, async (_, _) => await SnoozePrAsync(evt.PullRequestId, TimeSpan.FromHours(4)));
        menu.Items.Add("Mute PR permanently", null, async (_, _) => await MutePrAsync(evt.PullRequestId));
```

Replace with:

```csharp
        menu.Items.Add("Mark as read", null, async (_, _) =>
        {
            try { await _viewService.MarkReadAsync([evt.EventId]); await LoadEventsAsync(); }
            catch (Exception ex) { Log.Error(ex, "Mark read failed for event {EventId}", evt.EventId); }
        });
        menu.Items.Add("Snooze PR (1h)", null, async (_, _) =>
        {
            try { await SnoozePrAsync(evt.PullRequestId, TimeSpan.FromHours(1)); }
            catch (Exception ex) { Log.Error(ex, "Snooze failed for PR #{PrId}", evt.PullRequestId); }
        });
        menu.Items.Add("Snooze PR (4h)", null, async (_, _) =>
        {
            try { await SnoozePrAsync(evt.PullRequestId, TimeSpan.FromHours(4)); }
            catch (Exception ex) { Log.Error(ex, "Snooze failed for PR #{PrId}", evt.PullRequestId); }
        });
        menu.Items.Add("Mute PR permanently", null, async (_, _) =>
        {
            try { await MutePrAsync(evt.PullRequestId); }
            catch (Exception ex) { Log.Error(ex, "Mute failed for PR #{PrId}", evt.PullRequestId); }
        });
```

- [ ] **Step 4: Build and test**

```bash
dotnet build DevPulse.App -v q
dotnet test DevPulse.Tests -v q
```

Expected: 0 errors, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add DevPulse.App/Forms/InboxEventsForm.cs
git commit -m "fix: add top-level try/catch to all InboxEventsForm async handlers"
```

---

## Self-review

**Spec coverage check:**

| Audit finding | Task | Status |
|---|---|---|
| PASS1-CATEGORY4 CRITICAL: retry URL leak | Task 1 | ✅ |
| PASS1-CATEGORY3 MAJOR: WIQL path regex | Task 2 | ✅ |
| PASS2-CATEGORY9 MAJOR: PR pagination no warning | Task 3 | ✅ |
| PASS1-CATEGORY2 CRITICAL: Start() duplicate loop | Task 4 | ✅ |
| PASS1-CATEGORY1 CRITICAL: null Author crash | Task 5 | ✅ |
| PASS1-CATEGORY1 MAJOR: reviewer.Id empty guard | Task 5 | ✅ |
| PASS1-CATEGORY10 CRITICAL: PAT test client | Task 6 | ✅ |
| PASS1-CATEGORY3 CRITICAL: NumericUpDown clamp | Task 7 | ✅ |
| PASS1-CATEGORY2 MAJOR: BoardForm BeginInvoke | Task 8 | ✅ |
| PASS3-CATEGORY15 MAJOR: InboxEventsForm handlers | Task 9 | ✅ |
| PASS1-CATEGORY2 MAJOR: Dispose() sync deadlock | Already fixed in prior plan (uses .Dispose() which calls Wait(2s) with timeout) | ✅ |
| PASS1-CATEGORY2 MINOR: RunBackground cancellation | Design limitation — acceptable tradeoff, not actionable without major refactor | Deferred |
| PASS2-CATEGORY9 MINOR: tray menu full rebuild | Optimization, not a bug — deferred | Deferred |
| PASS3-CATEGORY11/12/13 NITs | Naming/doc improvements — deferred | Deferred |

**Placeholder scan:** No TBD, TODO, or incomplete steps found.

**Type consistency:** `WorkItemClient.ValidateWiqlPath` is named consistently across Task 2 implementation and tests. `CountingPoller` in tests correctly subclasses `PollingLoopBase` with the required abstract members.
