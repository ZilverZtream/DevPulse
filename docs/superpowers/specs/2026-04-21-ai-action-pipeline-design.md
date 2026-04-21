# AI Action Pipeline — Design Spec

**Status:** Approved (brainstorming, 2026-04-21)
**Target release:** MVP

## Summary

DevPulse gains an AI-assisted spec-drafting pipeline. When a developer discovers a newly-arrived "New" work item on the Kanban board, they can right-click it and have an AI provider (Claude Code CLI or OpenRouter) draft a structured implementation spec. The spec is saved to disk under `C:\devops\<project>\<ticketId>\`, recorded in SQLite for audit, and surfaced in an in-app review form. The feature is additive — no changes to existing polling, rule, or routing behaviour.

## Goals

- One-click AI spec generation for qualifying new work items (first-seen by DevPulse, state ∈ {New, Proposed}).
- Two providers at MVP: Claude Code CLI (local subprocess, local-data) and OpenRouter (HTTP, cloud-data).
- Persisted audit trail (DB + filesystem) of every generation attempt.
- Standardised markdown output with validated section structure.
- Settings-editable prompt templates with shipped defaults per work item type.
- User-editable output root path (default `C:\devops`).
- No auto-trigger, no background generation, no global "allow cloud" toggle — manual click is the consent.

## Non-goals (phase 2+)

- Auto-trigger modes (on-priority, on-label).
- Additional providers (Gemini CLI, Codex CLI, Ollama).
- Redaction / PII stripping.
- Attach-to-ADO-ticket via API.
- Template marketplace / cross-machine sync.

## 1. Architecture Overview

All additions fit within the existing `DevPulse.Core / DevPulse.Infrastructure / DevPulse.App` layering. No new projects.

### New types

**`DevPulse.Core.Interfaces`:**
- `IAiProvider` — the provider abstraction (see below).
- `IAiTemplateStore` — `GetTemplatesAsync / SaveTemplatesAsync / GetDefaultTemplateFor(workItemType)`.
- `IAiSpecWriter` — `WriteAsync(projectSlug, workItemId, timestamp, spec, prompt) → AiFilePaths`.
- `IAiAttemptStore` — `RecordAttemptAsync / GetAttemptsForWorkItemAsync`.

**`DevPulse.Core.Models`:**
- `AiTemplate` — `Id, Name, AppliesTo (List<string> work item types), RequiredHeaders (List<string>), PromptBody (string with tokens)`.
- `AiProviderProfile` — `ProviderId, Enabled, DefaultModel, ExecutablePath (CLI-only)`.
- `AiAttempt` — matches `ai_attempts` row; see §2.
- `AiFilePaths` — `SpecPath, PromptPath, MetaPath`.
- Enums `AiProviderKind { Cli, Http }`, `AiDataPolicy { Local, Cloud }`, `AiAttemptStatus { Success, ValidationFailed, ProviderError, Timeout }`. The `AiAttemptStatus` enum is serialized to DB as snake_case strings (`success | validation_failed | provider_error | timeout`) via a small dedicated converter in `SqliteStateStore` — parse back via a fixed dictionary, reject unknown values with a warning and default to `ProviderError`.

**`DevPulse.Core.Services`:**
- `AiOutputValidator` — pure class. `Validate(markdown, requiredHeaders) → ValidationResult { IsValid, MissingHeaders[], EmptySections[] }`.
- `AiTemplateRenderer` — pure class. `Render(template, workItem) → prompt string` with allow-listed token substitution.

**`DevPulse.Infrastructure.Ai` (new folder):**
- `ClaudeCliProvider : IAiProvider` — subprocess invocation.
- `OpenRouterProvider : IAiProvider` — HTTP invocation reusing existing `HttpClient` patterns.
- `FilesystemSpecWriter : IAiSpecWriter` — versioned writes under the configured root path.
- `SqliteStateStore` (existing) — implements `IAiAttemptStore`; adds `ai_attempts` table and `work_items.first_seen_utc` column.

**`DevPulse.App.Services`:**
- `AiPipelineService` — orchestrator. Single entry point `GenerateAsync(workItemId, templateId, providerId, ct) → AiAttempt`.

**`DevPulse.App.Forms`:**
- `AiGenerateDialog` — modal provider+template picker.
- `AiReviewForm` — non-modal rendered-markdown viewer with history.
- `BoardForm` (existing) — context-menu additions only.
- `SettingsForm` (existing) — new "AI" tab.

### Key interface

```csharp
public interface IAiProvider
{
    string Id { get; }                           // "claude-cli" | "openrouter"
    string DisplayName { get; }                  // "Claude Code CLI"
    AiProviderKind Kind { get; }                 // Cli | Http
    AiDataPolicy DataPolicy { get; }             // Local | Cloud
    Task<AiHealthResult> HealthCheckAsync(CancellationToken ct);
    Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct);
}

public sealed record AiGenerateRequest(string Prompt, string Model, TimeSpan Timeout);
public sealed record AiGenerateResult(string Markdown, string ModelUsed, int TokensIn, int TokensOut, TimeSpan Duration, string? ErrorMessage);
public sealed record AiHealthResult(bool Ok, string? ErrorMessage);
```

### Dependency direction

`Core` has no new NuGet deps. `Infrastructure` uses existing `HttpClient` (OpenRouter) and BCL `System.Diagnostics.Process` (Claude CLI). `App` depends on both as before.

## 2. Data Model & Storage

### SQLite schema changes

**New `ai_attempts` table:**

```sql
CREATE TABLE IF NOT EXISTS ai_attempts (
    id TEXT PRIMARY KEY,                    -- GUID
    work_item_id INTEGER NOT NULL,
    project TEXT NOT NULL,
    template_id TEXT NOT NULL,
    provider_id TEXT NOT NULL,              -- "claude-cli" | "openrouter"
    model TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL,                   -- success | validation_failed | provider_error | timeout
    validation_passed INTEGER NOT NULL DEFAULT 0,
    missing_sections TEXT NOT NULL DEFAULT '',  -- comma-separated
    spec_file_path TEXT NOT NULL DEFAULT '',
    prompt_file_path TEXT NOT NULL DEFAULT '',
    duration_ms INTEGER NOT NULL DEFAULT 0,
    tokens_in INTEGER NOT NULL DEFAULT 0,
    tokens_out INTEGER NOT NULL DEFAULT 0,
    created_at_utc TEXT NOT NULL,
    error_message TEXT
);
CREATE INDEX IF NOT EXISTS idx_ai_attempts_wi ON ai_attempts(work_item_id, created_at_utc DESC);
```

**`work_items.first_seen_utc TEXT` column** — additive migration using the existing `ALTER TABLE ADD COLUMN` + duplicate-column catch pattern. Set on insert, never on update.

**Schema version** — `DbSchema.CurrentSchemaVersion` bumps from `1` to `2`. `EnsureCreatedAsync` applies the additive migrations in sequence and updates `db_meta.schema_version`.

**`ai_attempts` is insert-only.** No UPDATE or DELETE in any code path. Regeneration inserts a new row.

### Filesystem layout

Output root configurable via `AppSettings.AiOutputRootPath` (default `C:\devops`).

```
C:\devops\<projectSlug>\<workItemId>\
  spec-20260421T143022Z.md
  prompt-20260421T143022Z.md
  spec-20260421T150811Z.md
  prompt-20260421T150811Z.md
  meta.json                               <- append-only array mirror of ai_attempts rows
```

- `projectSlug` = project name with non-alphanumeric chars replaced by `_` (helper `Slugify`); empty slug rejected.
- `workItemId` validated as positive integer.
- Final path built via `Path.Combine` + `Path.GetFullPath` + check resolved path is still under configured root — rejects `..` escapes.

### Settings (`kv_settings` JSON keys)

- **`AiTemplates`** — `List<AiTemplate>`.
- **`AiProviderProfiles`** — `List<AiProviderProfile>`.
- **`AiOutputRootPath`** (in `AppSettings`) — string, default `C:\devops`.

### Secrets (DPAPI)

- Existing: `%LOCALAPPDATA%\DevPulse\pat.dpapi`.
- **New**: `%LOCALAPPDATA%\DevPulse\openrouter.dpapi`.
- `SecretStore` extended from PAT-only to generic named secrets: `TryLoadSecret(name)` / `SaveSecret(name, value)`. PAT path preserved via `TryLoadSecret("pat")`.

## 3. Components

### Core

- **`IAiProvider`** and siblings — see §1.
- **`AiOutputValidator`** (pure). Parses H2 headers from the markdown (`^## (.+)$` regex). For each required header: present? body between this H2 and next H2 non-empty after trim? Returns `ValidationResult { IsValid, MissingHeaders, EmptySections }`. Case-insensitive header comparison. Unknown extra headers tolerated. Nested H3/H4 don't split sections.
- **`AiTemplateRenderer`** (pure). Substitutes allow-listed tokens in a template body: `{title}`, `{description}`, `{areaPath}`, `{iterationPath}`, `{type}`, `{state}`, `{acceptanceCriteria}`. Unknown tokens left literal AND a warning logged (catches template-injection attempts). Missing work item fields → empty substitution.

### Infrastructure

- **`ClaudeCliProvider`** — spawns `claude --print` via `System.Diagnostics.Process`. `UseShellExecute = false`. Prompt on stdin; no concatenation into `Arguments`. 90 s default timeout (configurable via provider profile). Captures stdout + stderr. On cancellation or timeout: `Process.Kill(entireProcessTree: true)` inside a `finally`. `DataPolicy = Local` (uses user's local Claude subscription).

- **`OpenRouterProvider`** — HTTP POST to `https://openrouter.ai/api/v1/chat/completions`. Bearer auth from DPAPI-loaded API key. Retry policy follows the same *pattern* as `AdoRetryHelper` (not a direct reuse — `AdoRetryHelper` is internal to `Infrastructure.AzureDevOps`): a `Stopwatch`-capped loop with 30 s total wall-clock budget. On 401/403: no retry, surface as auth error. On 429: respect `Retry-After` once, then fail. On 5xx: bounded retry within the wall-clock budget. `DataPolicy = Cloud`.

- **`FilesystemSpecWriter`** — creates output folder; writes `spec-<ts>.md` and `prompt-<ts>.md`; rewrites `meta.json` as a JSON array mirroring `ai_attempts` rows for this work item. Path traversal rejected before any IO.

- **`SqliteStateStore`** (existing, extended) — implements `IAiAttemptStore`. Adds `RecordAiAttemptAsync(AiAttempt)` (single INSERT under existing `_lock`) and `GetAiAttemptsForWorkItemAsync(int)` (SELECT ordered by `created_at_utc DESC`).

- **`SecretStore`** (existing, extended) — renamed PAT-specific helpers to generic `TryLoadSecret(name)` / `SaveSecret(name, value)`. PAT path preserved.

### App

- **`AiPipelineService`** — orchestrator. Single entry point `GenerateAsync(int workItemId, string templateId, string providerId, CancellationToken ct) → AiAttempt`. Sequence:
  1. Load work item, template, provider.
  2. Render prompt.
  3. Write `prompt-<ts>.md` to the output folder **before** calling provider.
  4. Call provider (with `ct`).
  5. Validate output (via `AiOutputValidator` using `template.RequiredHeaders`).
  6. Write `spec-<ts>.md` (regardless of validation pass/fail — user can inspect partial).
  7. Build `AiAttempt`, status per outcome.
  8. `RecordAttemptAsync` + rewrite `meta.json`.
  9. Return `AiAttempt`.

- **`AiGenerateDialog`** — modal ~420×320. Rows: template dropdown (pre-selected by work item type), provider dropdown (tagged `[LOCAL]`/`[CLOUD]`, disabled if unconfigured), model textbox (defaults from provider profile, editable per-call; hidden for Claude CLI), cloud warning label (shown when Cloud provider selected), Generate/Cancel.

- **`AiReviewForm`** — non-modal ~900×700, dark theme. Layout: top status banner (colour per `AiAttempt.status`), left history sidebar, center rendered-markdown view, right metadata card, bottom toolbar (Regenerate, Copy markdown, Open folder, Close). The markdown renderer is a small in-repo helper supporting only the subset AI specs produce: H1–H4 headings, paragraph text, unordered and ordered lists, inline `code`, fenced code blocks, and bold/italic inlines. Rendering target is a `RichTextBox` (no new NuGet). Tables, images, links-as-hyperlinks, and HTML are out of scope for MVP — if encountered, rendered as literal markdown with a one-line warning note at the top of the preview.

- **`BoardForm`** (existing, extended) — work item card context menu gains:
  - **"Draft spec with AI…"** — enabled when `first_seen_utc IS NOT NULL` AND `state ∈ FirstColumnMappedStates` AND ≥1 provider enabled.
  - **"View AI drafts…"** — enabled when any `ai_attempts` exist for this work item.
  - **"Open AI output folder"** — enabled when folder exists.

- **`SettingsForm`** (existing, extended) — new "AI" tab with:
  - Output root path (textbox + Browse).
  - Providers section: Claude Code CLI (Enabled, Executable path + Auto-detect, status pill); OpenRouter (Enabled, API key password field, Default model, Test connection, status pill).
  - Templates section: listbox + per-template editor (Name, Applies-to multi-select, Required headers, Prompt body with `{token}` hints), Reset/New/Delete buttons.
  - Save applies atomically via a new `SettingsService.SaveAiConfigAsync(List<AiProviderProfile>, List<AiTemplates>, CancellationToken)` that writes both `AiProviderProfiles` and `AiTemplates` KV keys in a single SQLite transaction. DPAPI secret save (OpenRouter API key) is a separate call ordered *after* the transactional KV write — if DPAPI fails, the user re-enters the key but provider/template config is already safe.

## 4. Data Flow

### Happy path

1. **Discovery.** `WorkItemPollingService` polls ADO. `UpsertWorkItemsAsync` extended to track new inserts and set `first_seen_utc = now` on insert only.
2. **Eligibility.** User right-clicks a work item card on `BoardForm`. Context menu checks eligibility; "Draft spec with AI…" enabled if all gates pass.
3. **Trigger.** `AiGenerateDialog` opens. Template pre-selected by work item type. User picks provider → clicks Generate.
4. **Pipeline.** `AiPipelineService.GenerateAsync` runs the 9-step sequence in §3. Prompt file written BEFORE provider call for debuggability. Attempt row always recorded, regardless of outcome.
5. **Review.** Dialog auto-closes on completion; `AiReviewForm` opens for that work item, loaded with the latest attempt. Status banner indicates success/validation_failed/provider_error/timeout.
6. **Cancellation.** Cancel button or dialog close cancels the token. CLI providers `Process.Kill(true)`. HTTP providers let `HttpClient` observe. Attempt recorded with appropriate status.
7. **Audit integrity.** On crash between `RecordAttemptAsync` and `meta.json` rewrite, next startup reconciles: for each work item with DB attempts but missing/stale `meta.json`, regenerate `meta.json` from DB rows.

## 5. Error Handling & Validation

### Validation

`AiOutputValidator` is pure. Takes markdown + `List<string> requiredHeaders` (from `AiTemplate`). For each required header: present? body non-empty? Returns structured result. `AiPipelineService` does NOT auto-retry on validation failure — records `validation_failed` and surfaces banner in review form; user decides whether to regenerate.

### Provider failure taxonomy

| Failure | Detected by | Status | User message |
|---|---|---|---|
| CLI missing | `HealthCheckAsync` pre-generate | blocked in dialog | "Claude CLI not found — Settings → AI" |
| CLI exit ≠ 0 | `Process.ExitCode` | `provider_error` | stderr tail, first 500 chars |
| CLI stdout empty | post-call | `provider_error` | "Provider returned no output" |
| CLI timeout | `ct` → `Process.Kill(true)` | `timeout` | "Generation exceeded {n}s timeout" |
| HTTP 401/403 | OpenRouterProvider | `provider_error` | "Auth failed — check API key" |
| HTTP 429 | retry once | `provider_error` | "Rate limited — try again in {n}s" |
| HTTP 5xx | bounded retry, 30 s cap | `provider_error` | "OpenRouter error: {status}" |
| Network timeout | `HttpClient.Timeout` | `timeout` | same as CLI timeout |
| Unexpected response JSON | OpenRouterProvider | `provider_error` | "Unexpected response shape" |
| Validation failure | `AiOutputValidator` | `validation_failed` | banner + missing-section list |
| Disk write failure | `FilesystemSpecWriter` | rethrow → dialog | "Could not write to {path}: {error}" |

### Invariants

- `prompt-<ts>.md` written before provider call (debuggability).
- Every generation attempt (success OR failure) produces exactly one `ai_attempts` row.
- `Process.Kill(entireProcessTree: true)` on timeout.
- `DataPolicy` surfaced in dialog before HTTP call fires.
- One `CancellationTokenSource` per attempt, owned by `AiGenerateDialog`.
- `ai_attempts` rows never mutated after insert.

## 6. Security & Consent

### API key storage

OpenRouter API key stored via `SecretStore` DPAPI wrapping at `%LOCALAPPDATA%\DevPulse\openrouter.dpapi`. Same encryption as existing PAT storage. Loaded once at `OpenRouterProvider` construction; rotating the key via Settings triggers a reload hook on the provider — no restart required.

### Consent surface

- Every provider declares `AiDataPolicy` at the code level.
- `AiGenerateDialog` tags each provider as `[LOCAL]` or `[CLOUD]` with Cloud entries visually marked.
- Cloud-provider selection shows a warning: *"This will send the work item title, description, and your prompt to {providerName} ({hostname}). Review your org's data policy."*
- No global "allow cloud" toggle. Manual click = consent. Record of consent = `ai_attempts` row.
- Unconfigured providers appear disabled in the dropdown with explanatory tooltips.

### Template data allow-list

Template renderer reads ONLY: `Title`, `Description`, `AreaPath`, `IterationPath`, `State`, `Type`, `AcceptanceCriteria`. Adding a new field requires a code change — no reflection, no dynamic dispatch.

### CLI invocation safety

`ProcessStartInfo` config:
- `FileName` = resolved absolute path (Settings-configured or `where claude` result).
- `Arguments` = only `--print` — never concatenated with prompt.
- `UseShellExecute = false`.
- `RedirectStandardInput / Output / Error = true`.
- `CreateNoWindow = true`.
- `WorkingDirectory` = work item's output folder.
- Prompt via stdin, avoiding command-line length and shell-quoting issues.

### Output folder safety

- `projectSlug` sanitized (non-alphanumeric → `_`).
- Path built via `Path.Combine` + `Path.GetFullPath` + under-root check — `..` escapes rejected.

### Audit immutability

`ai_attempts` has no UPDATE / DELETE code path. Regeneration inserts. Spec files are versioned per-timestamp; no overwrites.

## 7. UI Integration

See §3 (Components) for per-form layout. Summary:

- **BoardForm context menu** — 3 new items, each gated on eligibility; disabled tooltips explain gate failures.
- **AiGenerateDialog** — template picker + provider picker + model override + cloud warning + Generate/Cancel.
- **AiReviewForm** — status banner + history sidebar + rendered markdown + metadata card + toolbar. Non-modal. Regenerate is non-destructive (appends to history).
- **SettingsForm AI tab** — output root path, provider profiles, templates editor. Atomic save via new `SaveAiConfigAsync`.

## 8. Testing Approach

### Unit tests (xUnit, no external deps)

- `AiOutputValidator_Tests` — missing headers, empty sections, case-insensitive matching, nested H3/H4 don't split sections.
- `AiTemplateRenderer_Tests` — token substitution, allow-list enforcement, unknown tokens logged + empty-substituted.
- `FilesystemSpecWriter_Tests` (temp dir) — directory creation, file content, `meta.json` accumulation, path traversal rejection, slugify.
- `AiPipelineService_Tests` — mocks all dependencies. Happy path, provider exception, empty provider output, validation failure, cancellation, prompt-written-before-provider ordering, attempt-always-recorded parameterized across outcomes.

### Integration tests (real SQLite temp file)

- `SqliteAiAttemptStore_Tests` — record/retrieve, ordering, schema v1→v2 migration no-data-loss.
- `DbSchema_Tests` — idempotent `EnsureCreatedAsync`, `schema_version` updates.
- `WorkItemPollingService_FirstSeenStampedOnInsertOnly` — insert sets `first_seen_utc`; update doesn't.

### Provider tests

- `OpenRouterProvider_Tests` — `HttpMessageHandler` mock pattern already used for ADO tests. Assert request shape, parse responses, retry on 429/5xx, cancellation kills inflight.
- `ClaudeCliProvider_Tests` — uses a `FakeCliExecutable.cmd` fixture in `DevPulse.Tests\Fixtures\` that echoes stdin to stdout (and a second fixture that exits non-zero). Covers process lifecycle. Real Claude invocation stays manual.

### Manual verification checklist

- Right-click New work item → dialog → Generate → review form shows rendered markdown.
- Regenerate with different provider → history shows both.
- Break OpenRouter API key → auth error banner on next generate.
- Rename `claude` → CLI provider shows disabled.
- Template with required headers AI won't produce → validation banner in review form.

### Out of scope for MVP tests

- End-to-end with real providers (paid calls, done during development).
- UI snapshot tests (WinForms tooling weak; manual verification).

## Phasing

**Phase 1 (this spec, MVP):** Manual trigger only, Claude CLI + OpenRouter, shipped default templates with Settings editor, versioned output, full audit trail.

**Phase 2 (not this spec):**
- Auto-trigger modes (on-priority, on-label).
- Additional providers: Gemini CLI, Codex CLI, Ollama.
- Redaction rules.
- Attach-to-ADO-ticket via API.

## Open questions

None remain for Phase 1. All scope decisions resolved during brainstorming:
- Trigger semantics: first-seen + state ∈ {New, Proposed}.
- Providers: Claude CLI + OpenRouter.
- Trigger mode: manual only.
- Templates: shipped defaults, Settings-editable.
- Output contract: markdown with required H2 headers, validated by presence + non-empty body, no silent retry.
- Storage: versioned spec + prompt + `meta.json`, DB authoritative.
- Approach: additive service layer (Approach 1).
