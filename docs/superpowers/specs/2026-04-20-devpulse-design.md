# DevPulse — Design Document
**Date:** 2026-04-20  
**Spec version:** v0.1  
**Approach:** A — WinForms + Windows App SDK notifications (full implementation)

---

## 1. Purpose

DevPulse is a lightweight Windows tray application (.NET 8, WinForms) that monitors Azure DevOps pull request activity, classifies events into user-defined inboxes, and surfaces a dark-themed Kanban board of work items — all without requiring a browser to be open. Read-only toward Azure DevOps in v0.1.

---

## 2. Technology Choices

| Concern | Choice | Reason |
|---|---|---|
| UI framework | WinForms (.NET 8) | Spec-preferred for v0.1; simpler tray integration |
| Toast notifications | CommunityToolkit.WinUI.Notifications via Windows App SDK bootstrapper | Spec-named library; rich interactive toasts; no MSIX required |
| Local persistence | SQLite via Microsoft.Data.Sqlite | Spec-required |
| REST client | HttpClient + System.Text.Json | Spec-required |
| Logging | Serilog | Spec-recommended |
| PAT storage | Windows DPAPI (SecretStore) | Spec security requirement |
| Board UI style | Custom owner-drawn WinForms UserControls | Dark theme matching kanban_example.png reference |

---

## 3. Solution Structure

```
DevPulse.sln
├── DevPulse.Core/
│   ├── Models/
│   │   ├── DevOpsEvent.cs
│   │   ├── WorkItem.cs
│   │   ├── InboxDefinition.cs
│   │   ├── InboxRule.cs
│   │   ├── Watcher.cs
│   │   ├── KeywordPack.cs
│   │   ├── IdentityAlias.cs
│   │   ├── MuteEntry.cs
│   │   ├── BoardColumnDefinition.cs
│   │   └── AppSettings.cs
│   ├── Enums/
│   │   ├── DevOpsEventType.cs
│   │   ├── EventSource.cs
│   │   ├── EventMeaning.cs
│   │   ├── WorkItemType.cs
│   │   ├── AgingLevel.cs
│   │   ├── MuteScope.cs
│   │   └── WatcherType.cs
│   ├── Interfaces/
│   │   ├── IStateStore.cs
│   │   ├── IAzureDevOpsClient.cs
│   │   ├── IWorkItemClient.cs
│   │   └── INotificationService.cs
│   └── Services/
│       ├── RuleEngine.cs
│       ├── EventNormalizer.cs
│       ├── WorkItemNormalizer.cs
│       ├── IdentityNormalizer.cs
│       ├── EventCollapser.cs
│       ├── MuteService.cs
│       ├── InboxViewService.cs
│       ├── BoardViewService.cs
│       └── DebugLogService.cs
├── DevPulse.Infrastructure/
│   ├── AzureDevOps/
│   │   ├── AzureDevOpsClient.cs
│   │   ├── WorkItemClient.cs
│   │   ├── AzureDevOpsDtos.cs
│   │   ├── WorkItemDtos.cs
│   │   └── ApiVersions.cs
│   ├── Persistence/
│   │   ├── SqliteStateStore.cs
│   │   └── DbSchema.cs
│   ├── Notifications/
│   │   └── WindowsToastNotificationService.cs
│   └── Security/
│       └── SecretStore.cs
└── DevPulse.App/
    ├── Program.cs
    ├── TrayApplicationContext.cs
    ├── Forms/
    │   ├── SettingsForm.cs
    │   ├── InboxEventsForm.cs
    │   ├── BoardForm.cs
    │   └── DebugWindow.cs
    ├── Services/
    │   ├── PollingService.cs
    │   ├── WorkItemPollingService.cs
    │   ├── NotificationService.cs
    │   └── SettingsService.cs
    └── UI/
        ├── TrayMenuBuilder.cs
        ├── BoardColumnPanel.cs
        └── WorkItemCard.cs
```

**Dependency rule:** `Core` has no external NuGet dependencies beyond BCL. `Infrastructure` references `Core` only. `App` references both.

---

## 4. Architecture

### 4.1 Components

| Component | Project | Responsibility |
|---|---|---|
| TrayApplicationContext | App | App lifecycle, tray icon, context menu, unread counts |
| PollingService | App | Schedules PR refresh cycles with overlap guard |
| WorkItemPollingService | App | Schedules work item refresh cycles with overlap guard |
| IdentityNormalizer | Core | Resolves ADO identities → canonical keys; sets EventSource |
| AzureDevOpsClient | Infrastructure | PR REST endpoints and DTO mapping |
| WorkItemClient | Infrastructure | Work item REST endpoints and DTO mapping |
| EventNormalizer | Core | Converts ADO entities → DevOpsEvent; sets EventMeaning |
| WorkItemNormalizer | Core | Converts ADO DTOs → WorkItem; computes DaysInCurrentState |
| RuleEngine | Core | Evaluates watchers → NeedsMyAttention → user inboxes in order |
| EventCollapser | Core | Groups related events within a poll cycle into collapsed rows |
| MuteService | Core | Tracks and enforces PR and author mutes/snoozes |
| NotificationService | App | Bridges to WindowsToastNotificationService |
| SqliteStateStore | Infrastructure | Persists all local state to SQLite |
| InboxViewService | Core | Loads latest events for a selected inbox |
| BoardViewService | Core | Groups work items by column; computes aging |
| SettingsService | App | Loads and saves AppSettings, inboxes, columns, packs, aliases |
| DebugLogService | Core | Records poll results, rule traces, identity decisions (last 500, in-memory) |
| SecretStore | Infrastructure | DPAPI-backed PAT storage |
| ApiVersions | Infrastructure | Single static class for all ADO REST API version strings |

### 4.2 PR Data Flow

```
PollingService (timer tick, overlap guard)
  → AzureDevOpsClient.GetRelevantPullRequestsAsync()
  → for each PR: compare status/votes with pr_snapshots → emit state-change events
  → AzureDevOpsClient.GetPullRequestThreadsAsync() → emit comment events
  → IdentityNormalizer.Normalize(all authors) → sets EventSource
  → EventNormalizer.SetMeaning(each event) → sets EventMeaning
  → StateStore.EventExistsAsync() → drop already-known EventIds
  → MuteService.Filter() → drop muted/snoozed
  → EventCollapser.Collapse() → group by PR+source within poll window
  → RuleEngine.Assign() → watchers → NeedsMyAttention → user inboxes
  → StateStore.SaveEventsAsync() → persist with InboxName + MatchedRuleDescription
  → NotificationService.ShowAsync() → toast where inbox.ShowNotifications = true
  → TrayApplicationContext.RefreshUnreadCounts()
  → DebugLogService.Record() → rule trace per event
  → StateStore.SetLastSuccessfulPollAsync("prs", now)
```

### 4.3 Work Item Data Flow

```
WorkItemPollingService (timer tick, overlap guard)
  → WorkItemClient.GetWorkItemsAsync(areaPath, iterationPath)
  → WorkItemNormalizer.Normalize() → DaysInCurrentState, AgingLevel, LinkedPRId
  → StateStore.UpsertWorkItemsAsync()
  → BoardViewService.GroupByColumn() → applies aging thresholds
  → BoardForm.RefreshIfOpen() → stale-data banner cleared
  → StateStore.SetLastSuccessfulPollAsync("workitems", now)
```

---

## 5. Data Models (as defined in spec)

### DevOpsEvent
Stores a single normalized PR event with deduplication key (`EventId`), classification (`EventSource`, `EventMeaning`), inbox assignment, collapse metadata, and read/notification flags.

**EventId patterns:**
- Comment: `pr:{prId}:thread:{threadId}:comment:{commentId}`
- PR state: `pr:{prId}:status:{status}:at:{timestamp}`
- Reviewer added: `pr:{prId}:reviewer:{reviewerId}:added`
- Vote changed: `pr:{prId}:reviewer:{reviewerId}:vote:{vote}:at:{ts}`
- Collapsed group: `pr:{prId}:collapsed:{source}:poll:{pollTimestamp}`

### WorkItem
Stores normalized work item with computed `DaysInCurrentState` (from `StateChangedAtUtc`), `AgingLevel`, and optional `LinkedPullRequestId`.

### InboxDefinition / InboxRule / Watcher / KeywordPack / IdentityAlias / MuteEntry
As defined in spec sections 8–9. Stored as JSON blobs in SQLite settings tables.

---

## 6. Rule Engine

Evaluation order (enforced exclusively by RuleEngine — no other component assigns inboxes):
1. **Watchers** — short-circuit; matching watcher assigns inbox immediately
2. **NeedsMyAttention** (system inbox, always first) — evaluated against default catch conditions
3. **User inboxes** in Order — first match wins
4. **Fallback inbox** (no conditions) — always last

Rule logic:
- All non-empty include conditions AND'd together
- Any exclude condition → no match regardless of includes
- Inbox matches if any rule matches (OR across rules)

---

## 7. UI Design

### 7.1 Kanban Board (dark theme — reference: kanban_example.png)

**Color palette:**
- Window background: `#1E1E2E`
- Column panel background: `#2A2A3C`
- Card background: `#32324A`
- Card border: `#444466`
- Text primary: `#E0E0F0`
- Text secondary: `#9090B0`
- Accent blue (PR link): `#5B9BD5`

**Column header:** colored status dot (blue/gray/orange/purple/green) + column name + item count badge

**WorkItemCard UserControl (owner-drawn):**
- Bold `#ID` + title (white, truncated with ellipsis)
- Type badge pill: Task=`#4A7DA8`, Feature=`#5A4FA8`, Bug=`#C0522A`, Story=`#2A8A6A`
- Priority badge pill: P1=`#C03030`, P2=`#C09030`, P3=`#30A030`
- Assignee initials circles: deterministic color from hash of canonical key
- PR link text in accent blue when `LinkedPullRequestId` resolved
- Aging badge: amber pill (Aging), red pill + card border highlight (Stale)

**Filter bar:** `TextBox` (search) + `ComboBox` × 3 (All types / All assignees / All priorities), all dark-styled. Non-matching cards dimmed (not hidden) so column counts remain visible.

**Quick-toggle toolbar buttons:** Mine only | Current sprint | Bugs only | Unassigned only

### 7.2 Tray Menu Structure
```
Refresh now ▶
  └ Refresh PRs
  └ Refresh board
View latest ▶
  └ Needs My Attention  (N)
  └ [user inboxes]  (N)
Open board
Muted PRs
Open Azure DevOps
Debug window
Settings
Exit
```

### 7.3 Settings Form Tabs
Connection | Polling | Identities | Inboxes | Board | Notifications | Advanced

### 7.4 Debug Window Tabs
Poll status | Event log | Rule traces | Identity log | Mute log  
Read-only, last 500 events in memory, cleared on restart, copy-to-clipboard per entry.

---

## 8. Storage

**Location:** `%LOCALAPPDATA%\DevPulse\devpulse.db`  
**Schema init:** `DbSchema.EnsureCreatedAsync()` on first launch

| Table | Contents |
|---|---|
| events | DevOpsEvent rows; indexed by EventId and InboxName |
| work_items | WorkItem rows; upserted by Id |
| mute_entries | MuteEntry rows; expired entries cleaned on each poll |
| poll_state | Last successful poll timestamp keyed by track |
| pr_snapshots | Last known PR status/vote snapshot for change detection |
| settings | JSON blob: AppSettings |
| inbox_definitions | JSON blob: List\<InboxDefinition\> |
| board_columns | JSON blob: List\<BoardColumnDefinition\> |
| keyword_packs | JSON blob: List\<KeywordPack\> |
| identity_aliases | JSON blob: List\<IdentityAlias\> |
| watchers | JSON blob: List\<Watcher\> |

---

## 9. Security

- PAT stored via DPAPI (`SecretStore`) — never in SQLite or logs
- Settings JSON export redacts PAT unless user explicitly confirms
- Identity alias data stays local — never sent to Azure DevOps

---

## 10. Error Handling

- Network/auth failures: log, keep app running, retry on next interval
- Auth failure: suppress repeated popups, mark disconnected, surface Settings link
- Work item poll failure: board shows non-intrusive stale-data banner; cached data stays
- PR poll failure: inbox views show last cached events
- All exceptions recorded in DebugLogService with context

---

## 11. Default Configuration (seeded on first launch)

| Setting | Default |
|---|---|
| PR polling interval | 5 minutes |
| Work item polling interval | 10 minutes |
| Inbox: Needs My Attention | System, always first, notifications always on |
| Inbox: Code Rabbit | EventSource = Bot, notifications off |
| Inbox: Merged PRs | EventMeaning = Merged, notifications on |
| Inbox: Prioritized | Fallback catch-all, notifications on |
| Keyword pack: needs-attention | needs test, please review, ready for QA, please verify, ready for test |
| Keyword pack: blocking | changes requested, blocked, waiting for author, do not merge |
| Board columns | Feature Request / Backlog / Doing / In Review / Done (per spec states) |
| Aging warning | 2 days |
| Aging stale | 6 days |
| Max events per inbox | 100 |
| Debug log retention | 500 events, cleared on restart |

---

## 12. MVP Acceptance Criteria

All 18 criteria from spec section 19 must pass:
- Configure org/project/PAT/intervals/identity → app runs in tray with icon and menu
- PR polling detects: new comments, vote changes, PR completed, PR abandoned
- NeedsMyAttention is present, non-deletable, evaluated first
- Bot comments excluded from NeedsMyAttention and Prioritized
- Inbox rules support ExcludeAuthorContains and MessageContainsAny
- Tray menu shows per-inbox unread counts
- Events notify once only; restart never re-notifies
- Mark read individually and mark all per inbox
- Mute PR permanently; snooze PR for configurable duration
- Bot events on same PR in same poll cycle collapse into one row
- Kanban board opens from tray, shows items in correct columns
- Board cards show days-in-state and aging badge above threshold
- Board quick-toggle filters work correctly
- "Open linked work item" offered when link is resolved
- Debug window shows rule trace per event
- Rule test mode predicts inbox assignment correctly
