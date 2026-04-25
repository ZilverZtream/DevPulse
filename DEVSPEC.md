# DevPulse v1.0 Engineering Spec

Living document. Tracks every issue discovered during the v1.0 hardening pass and the order they get fixed in. Findings come from two waves of parallel agent audits + verification grep/build.

Legend:
- **P0** — security, data loss, hang/crash, blocks ship
- **P1** — visible bug, missing core UX, perf cliff
- **P2** — polish, nice-to-have

Status: ☐ pending · ▶ in progress · ✓ done

---

## v1.0 Definition of Done

1. Build clean, all tests green.
2. Every P0 closed.
3. P1 UX baseline: sortable/filterable inbox + debug grids, first-run wizard, error tray indicator, window position persistence.
4. P1 data hardening: indexes added, retry loop fixed, batched event-id check, glob cache eviction, redirect blocking.
5. Test coverage on the three critical untested classes (SqliteStateStore concurrency, PollingService dedup/collapse, AdoRetryHelper backoff math).
6. Manual smoke test pass on the BoardForm + Settings + AI flow.

---

## Wave A — Security & data integrity (P0)

| # | File:line | Issue | Fix |
|---|-----------|-------|-----|
| A1 | `Infrastructure/AzureDevOps/AzureDevOpsAuthHandler.cs` | No `AllowAutoRedirect = false`. PAT can leak to attacker host on 3xx. | Disable redirects on the inner handler; log + drop redirects. |
| A2 | `Core/Services/MarkdownRenderer.cs` Escape() | Only escapes `\{}`; user content can inject RTF control words (`\objdata`, `\bin`). | Whitelist plain-text characters; escape every byte > 127 as `\uNNNN`; strip control sequences. |
| A3 | `App/Services/SettingsService.cs` SaveAppSettingsAsync | `Project` not run through `WiqlPathGuard`. WIQL injection via project name. | Validate Project (and any other path-shaped value) before persist. |
| A4 | `Infrastructure/Ai/FilesystemSpecWriter.cs` | Path bounds check is `StartsWith` only; symlinks/junctions bypass. | After resolving full path, reject if any segment is a reparse point. |
| A5 | `Infrastructure/Security/SecretStore.cs` | DPAPI file inherits ACLs from %LOCALAPPDATA%; world-readable in some setups. | Set explicit `FileSystemAccessRule(currentUser, FullControl, Allow)` after write; deny inherited rules. |

## Wave B — Resource & lifetime leaks (P0/P1)

| # | File:line | Issue | Fix |
|---|-----------|-------|-----|
| B1 | `App/UI/WorkItemCard.cs:35-39` | 5 static `Font` objects never disposed → GDI handle leak (long-running). | Move to a static `GdiCache` with proper shutdown disposal; or accept-as-app-lifetime but document. |
| B2 | `App/UI/WorkItemCard.cs:54-122` | `OnPaint` allocates new `SolidBrush`/`Pen`/`StringFormat`/`GraphicsPath` every frame. | Cache static brushes/pens/format objects; precompute rounded-rect path. |
| B3 | `App/UI/WorkItemCard.cs:51` | `BuildContextMenu` per card; not disposed when container clears. | Dispose context menu in `Dispose(bool)` override. |
| B4 | `App/UI/BoardColumnPanel.cs:79` | `new SolidBrush(dot)` per repaint per column. | Cache brushes keyed by dot color. |
| B5 | `App/Forms/AiGenerateDialog.cs:15` | `_cts` not disposed (only Cancel on FormClosing). | Add `Disposed += ... _cts.Dispose();` |
| B6 | `App/TrayApplicationContext.cs:169,411` | `_aiHttpClient` disposed only in `Dispose()`, not in `OnApplicationExit`. | Move HttpClient cleanup into `OnApplicationExit`. |
| B7 | `App/UI/WorkItemCard.cs:155-156` | `GetInitials` allocates strings every paint. | Compute on construction; cache in field. |

## Wave C — Threading & async hygiene (P0)

| # | File:line | Issue | Fix |
|---|-----------|-------|-----|
| C1 | `App/TrayApplicationContext.cs:399-401, 414-416` | `.AsTask().Wait()` on UI thread during shutdown. | Replace with proper async cleanup; do best-effort with timeout, no blocking. |
| C2 | `App/Services/PollingLoopBase.cs:92` | Same `.Wait()` pattern in sync `Dispose`. | Drop sync wait; rely on `DisposeAsync`. |
| C3 | `App/Forms/DebugWindow.cs:115` | `settingsTask.Result` on UI thread. | Make Refresh async; await. |
| C4 | `App/Forms/SettingsForm.cs:36` | `SecretStore.LoadPat()` blocks UI thread. | Move to async load; show "loading" state in PAT field. |
| C5 | `Infrastructure/Persistence/SqliteStateStore.cs` | `SemaphoreSlim` disposed without flag → `ObjectDisposedException` race. | Add `_disposed` flag, check before `WaitAsync`. |
| C6 | `App/TrayApplicationContext.cs:133-138` | `PollCompleted` lambdas never unsubscribed. | Store delegate, unsubscribe in cleanup. |
| C7 | App-tier async code | Missing `ConfigureAwait(false)` in non-UI paths (Infrastructure/Core). | Sweep Core+Infrastructure to add. |

## Wave D — Data layer hardening (P0/P1)

| # | File:line | Issue | Fix |
|---|-----------|-------|-----|
| D1 | `Infrastructure/Persistence/DbSchema.cs` | Missing index on `events(inbox_name, discovered_at_utc DESC)` and `events(event_source, event_meaning)`. | Add migration v3 with composite indexes. |
| D2 | `Infrastructure/AzureDevOps/AdoRetryHelper.cs:32-48` | 429 retried only once. | Loop 429-retry within wall-clock cap; honor Retry-After (clamp 60s). |
| D3 | `Infrastructure/Persistence/SqliteStateStore.cs:43-65` | Event-id existence check chunked into N round-trips (500-row chunks). | Single batch UNION or temp-table approach; verify EXPLAIN. |
| D4 | `Core/Services/RuleEngine.cs:199` | Glob cache silently stops growing at 256, recompiles every time after. | LRU eviction; `Dictionary<string, Regex>` + linked-list, evict oldest. |
| D5 | `Infrastructure/AzureDevOps/AzureDevOpsClient.cs:54` | 1000-PR cap silently truncates. | Warn loudly when hit; expose flag in poll status; configurable. |
| D6 | `Core/Models/AppSettings.cs:23` | Hardcoded `C:\devops` path. | Default to `%USERPROFILE%\Documents\DevPulse\specs`. |

## Wave E — Error UX & first-run (P1)

| # | What | How |
|---|------|-----|
| E1 | Tray icon health states (green/yellow/red) | Compose icon at runtime: base + colored dot overlay; recompute on each `PollCompleted`. |
| E2 | First-run wizard | New `FirstRunForm`: org URL → PAT → "Test connection" → choose project → seed default inbox. |
| E3 | Auth-failure prompt | On 401, surface non-modal banner in `BoardForm` + tray balloon "PAT expired — open Settings". |
| E4 | Settings JSON corruption surface | On load failure, write `.bak` next to the file, reset to defaults, show one-time tray balloon + log. |
| E5 | Error transience enum | Replace `PollErrorClassifier.Classify(string)` → `enum PollErrorKind { Transient, AuthRequired, Permanent, Throttled }`. |
| E6 | "Loading…" spinner | Lightweight indeterminate progress overlay used by long async loads (board, inbox, settings). |

## Wave F — UI overhaul: sortable lists & polish (P1)

| # | What | How |
|---|------|-----|
| F1 | Shared `SortableListView` (dark-themed, owner-drawn) | New `App/UI/SortableListView.cs`: column click sort, alt-row stripes, focus ring, Ctrl+C copy, F5 refresh hook. |
| F2 | InboxEventsForm uses F1 | Replace existing `ListView`. |
| F3 | DebugWindow grids use F1 | Replace 5× DataGridView. |
| F4 | Settings DataGridViews → +/− and reorder | Add inline button strip below alias and column grids; up/down keyboard reorder. |
| F5 | Window position persistence | Save bounds in settings KV per form; validate against `Screen.AllScreens` on load. |
| F6 | Tooltips on truncated work item titles | `ToolTip` on `WorkItemCard` (full title + assignee + created date). |
| F7 | Keyboard shortcuts | F5 refresh, Ctrl+F search focus, Esc clear filter, Enter to open card, Ctrl+, settings. |
| F8 | Filter debounce | Search box change → 250ms debounce. |
| F9 | High-DPI fixes | Replace hardcoded heights/widths with `LogicalToDeviceUnits`; verify at 125/150/200%. |

## Wave G — Missing features (P1/P2)

| # | What | How |
|---|------|-----|
| G1 | Auto-start with Windows | Settings checkbox writes/removes `HKCU\…\Run\DevPulse`. |
| G2 | Quiet hours / DND | New settings group: schedule + per-day mask. PollingService skips notifications during quiet hours. |
| G3 | Snooze "until tomorrow / Monday" | Extend `MuteService` snooze presets in tray context menu and InboxEventsForm. |
| G4 | Actionable toasts | `ToastContentBuilder.AddButton("Open", …)`/`("Snooze", …)`; handle activation in TrayApplicationContext. |
| G5 | Settings tooltips | Add `ToolTip` for every input on `SettingsForm`. |
| G6 | Export poll diagnostics | DebugWindow "Export logs" → JSON + CSV. |
| G7 | Statistics tab | Simple "PRs reviewed last 7d / events per inbox / failed polls" — read from SQLite. |

## Wave H — Test coverage (P1)

| # | What |
|---|------|
| H1 | `SqliteStateStoreConcurrencyTests` — parallel `SaveEventsAsync` + `Get*Async` race + Dispose-during-wait |
| H2 | `AdoRetryHelperTests` — 429 retry loop within wall-clock cap; exponential backoff math; cancellation |
| H3 | `PollingServiceDedupTests` — first-seen suppression of comments; collapsed event id stability; mute filtering |
| H4 | `MarkdownRendererSecurityTests` — RTF injection vectors stay neutralized |
| H5 | `WiqlPathGuardTests` — extended cases (Project, double-quote, exotic Unicode) |
| H6 | `FilesystemSpecWriterTests` — symlink/junction rejection on Windows |
| H7 | `RuleEngineGlobCacheTests` — LRU eviction at cap; recompile-storm regression |

## Wave I — Cleanup / commit hygiene

- Remove dead code: `is_collapsed` schema column (replace with VIEW or migrate); unused `BuildReviewerAddedEventId`.
- Add CI smoke build script (msbuild + tests).
- Update `CLAUDE.md` with new conventions (GdiCache, SortableListView, telemetry).

---

## Execution log

(filled in as work progresses)
