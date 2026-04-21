# AI Pipeline MVP — Manual Smoke Test

Run after all 23 tasks land. Covers the workflows no automated test reaches (real CLI / real HTTP, WinForms UI interactions).

## Pre-flight

- [ ] `dotnet build --nologo` — 0 warnings, 0 errors
- [ ] `dotnet test --nologo --no-build` — all tests pass
- [ ] Claude Code CLI installed and on PATH: `where claude` returns a path
- [ ] OpenRouter account with a valid API key in hand

## Settings configuration

- [ ] Open Settings → AI tab. Tab loads without error.
- [ ] "Auto-detect" button under Claude CLI populates the path from `where claude`.
- [ ] Check "Claude Code CLI" checkbox.
- [ ] Paste OpenRouter API key (UseSystemPasswordChar hides it).
- [ ] Set OpenRouter model to `anthropic/claude-3.5-sonnet` (or `openai/gpt-4o-mini` if you prefer).
- [ ] Check "OpenRouter (HTTP)" checkbox.
- [ ] Verify the Templates listbox shows the 4 shipped defaults (Bug, User Story, Feature, Task).
- [ ] Click a template → "Required headers" and "Template body" fields populate.
- [ ] Click Save. No error dialog. Settings persist — close and reopen Settings, verify values are still there.

## Happy path — Claude CLI (local)

- [ ] Wait for a work item poll to complete (or trigger via Refresh). At least one New/Proposed work item on the board.
- [ ] Right-click the card → "Draft spec with AI…" is enabled (tooltip: "Draft an AI spec...").
- [ ] Dialog opens. Header reads `Work item #<id> — <title>`.
- [ ] Template dropdown pre-selects the one matching work item type (Bug → Bug template, etc.).
- [ ] Provider dropdown lists `[LOCAL] Claude Code CLI` and `[CLOUD] OpenRouter`. Cloud warning band is hidden when Local is selected.
- [ ] Click Generate. Button changes to "Generating…". Dialog closes on completion.
- [ ] AiReviewForm opens. Green status banner shows "Generated via claude-cli — N ms — tokens in/out 0/0".
- [ ] Rendered markdown fills the center panel. All 6 H2 headings visible.
- [ ] Metadata sidebar shows template id, provider id, model (may be empty for CLI), token counts, duration.
- [ ] Output folder exists at `C:\devops\<project-slug>\<work-item-id>\` with `spec-<ts>.md`, `prompt-<ts>.md`, `meta.json`.
- [ ] `meta.json` is valid JSON array with one entry; `status` field is `"success"`, `validation_passed` is `1`, `spec_file_path` and `prompt_file_path` point to the same files.

## Happy path — OpenRouter (cloud)

- [ ] Right-click the same (or a different) work item → "Draft spec with AI…".
- [ ] Pick `[CLOUD] OpenRouter` from the provider dropdown.
- [ ] Cloud warning band appears: *"This will send the work item title, description, and your prompt to OpenRouter. Review your org's data policy."*
- [ ] Click Generate. Wait for completion (may take a few seconds longer than CLI).
- [ ] Review form opens; banner is green; token counts are non-zero (OpenRouter returns usage).
- [ ] If you generated for a work item with prior attempts, the history sidebar lists both; newest first.
- [ ] Click an older entry → the rendered spec switches to that attempt's content.

## Regenerate flow

- [ ] From an open AiReviewForm, click "Regenerate". AiGenerateDialog reopens.
- [ ] Pick a different template or provider. Generate.
- [ ] On return, history sidebar now shows N+1 entries.
- [ ] Files on disk: one new `spec-<newTs>.md` + one new `prompt-<newTs>.md`; `meta.json` array has grown by one element.

## Error paths

- [ ] Empty OpenRouter API key → pick OpenRouter → Generate → red banner "Provider error: OpenRouter API key not configured" (or similar). Review form still opens with the failure recorded.
- [ ] Invalid OpenRouter key → generate → red banner with "401" in the message.
- [ ] Rename `claude.exe` temporarily → reopen Settings → AI → Auto-detect finds nothing → "claude not found on PATH." OR: provider dialog disables the CLI option.
- [ ] Edit a template to include a required header name the AI won't emit (e.g., "Zebra") → Generate → orange banner "Validation failed — missing: Zebra". Spec file is still written; user can inspect.

## Eligibility gating

- [ ] Move a work item to "Active" in ADO → wait for next poll → right-click the card → "Draft spec with AI…" is disabled. Tooltip: "AI drafts are only available for first-seen New/Proposed items".
- [ ] "View AI drafts…" is always clickable; opens the review form with any existing attempts.
- [ ] "Open AI output folder" opens Explorer to the work item's folder if attempts exist.

## First-seen behavior

- [ ] On a fresh DB (delete `%LOCALAPPDATA%\DevPulse\devpulse.db` before launching) and after the first poll: every work item has a `first_seen_utc` value (query via any SQLite browser: `SELECT id, first_seen_utc FROM work_items LIMIT 5`).
- [ ] Change a work item's state in ADO and re-poll → `first_seen_utc` for that item is unchanged (preserved across upserts).

## Audit trail

- [ ] `ai_attempts` table has exactly one row per generation — success AND failure.
- [ ] `meta.json` in each work item folder mirrors the DB's ordering.
- [ ] Delete `meta.json` manually → reopen review form → history still displays correctly (DB is source of truth).

## Shutdown

- [ ] Click X on tray menu → app closes cleanly. No zombie `claude.exe` processes (check Task Manager).
- [ ] Relaunch → settings still load correctly; no schema errors on startup.

## Known limitations (not bugs)

- CLI provider does not populate `ModelUsed` or token counts (Claude CLI doesn't expose them via stdout).
- `OpenRouterProvider` retries once on 429; persistent rate-limits will surface as provider error.
- Max mute duration caps don't apply to AI attempts (attempts are insert-only, never TTL'd).
- No automatic cleanup of old spec files — user manages `C:\devops\` folder manually.
