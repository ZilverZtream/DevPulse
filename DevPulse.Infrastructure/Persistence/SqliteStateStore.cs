using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DevPulse.Infrastructure.Persistence;

public sealed class SqliteStateStore : IStateStore, IAiAttemptStore, IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _disposed;

    public SqliteStateStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        using var pragma = _conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
    }

    public async Task InitializeAsync() => await DbSchema.EnsureCreatedAsync(_conn);

    // ── Events ────────────────────────────────────────────────────────────────

    public async Task<bool> EventExistsAsync(string eventId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM events WHERE event_id = @id";
            cmd.Parameters.AddWithValue("@id", eventId);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
        }
        finally { _lock.Release(); }
    }

    // SQLite default SQLITE_LIMIT_VARIABLE_NUMBER is 999 (999 in 3.32+, 250000 hard ceiling). 900
    // leaves head-room for any wrapper params and avoids brushing the limit on bespoke builds.
    private const int InClauseChunkSize = 900;

    public async Task<HashSet<string>> GetExistingEventIdsAsync(IEnumerable<string> candidateIds, CancellationToken ct = default)
    {
        var ids = candidateIds.ToList();
        if (ids.Count == 0) return [];
        await _lock.WaitAsync(ct);
        try
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in ids.Chunk(InClauseChunkSize))
            {
                await using var cmd = _conn.CreateCommand();
                var paramNames = chunk.Select((_, i) => $"@p{i}").ToList();
                cmd.CommandText = $"SELECT event_id FROM events WHERE event_id IN ({string.Join(",", paramNames)})";
                for (int i = 0; i < chunk.Length; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    result.Add(reader.GetString(reader.GetOrdinal("event_id")));
            }
            return result;
        }
        finally { _lock.Release(); }
    }

    public async Task<HashSet<string>> GetReadEventIdsAsync(IEnumerable<string> eventIds, CancellationToken ct = default)
    {
        var ids = eventIds.ToList();
        if (ids.Count == 0) return [];
        await _lock.WaitAsync(ct);
        try
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var chunk in ids.Chunk(InClauseChunkSize))
            {
                await using var cmd = _conn.CreateCommand();
                var paramNames = chunk.Select((_, i) => $"@p{i}").ToList();
                cmd.CommandText = $"SELECT event_id FROM events WHERE event_id IN ({string.Join(",", paramNames)}) AND is_read = 1";
                for (int i = 0; i < chunk.Length; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    result.Add(reader.GetString(reader.GetOrdinal("event_id")));
            }
            return result;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveEventsAsync(IEnumerable<DevOpsEvent> events, CancellationToken ct = default)
    {
        var list = events.ToList();
        if (list.Count == 0) return;
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);
            await using var cmd = _conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            // is_collapsed column is dead (readers derive IsCollapsed from CollapsedCount > 1).
            // We don't include it here — SQLite uses the schema DEFAULT 0.
            cmd.CommandText = """
                INSERT OR IGNORE INTO events (
                    event_id, event_type, event_source, event_meaning,
                    inbox_name, collapsed_count,
                    pull_request_id, pull_request_title, pull_request_url,
                    organization, project, repository,
                    author_display_name, author_canonical_key, message_text, status,
                    created_at_utc, discovered_at_utc, source_thread_id, source_comment_id,
                    linked_work_item_id, notification_sent, is_read, matched_rule_description,
                    is_current_user_reviewer
                ) VALUES (
                    @eid, @etype, @esrc, @emean,
                    @inbox, @cnt,
                    @prid, @prtitle, @prurl,
                    @org, @proj, @repo,
                    @adisplay, @acanon, @msg, @status,
                    @created, @disc, @tid, @cid,
                    @linked, @notif, @read, @rule,
                    @reviewer
                )
                """;
            cmd.Parameters.AddWithValue("@eid", string.Empty);
            cmd.Parameters.AddWithValue("@etype", 0);
            cmd.Parameters.AddWithValue("@esrc", 0);
            cmd.Parameters.AddWithValue("@emean", 0);
            cmd.Parameters.AddWithValue("@inbox", string.Empty);
            cmd.Parameters.AddWithValue("@cnt", 0);
            cmd.Parameters.AddWithValue("@prid", 0);
            cmd.Parameters.AddWithValue("@prtitle", string.Empty);
            cmd.Parameters.AddWithValue("@prurl", string.Empty);
            cmd.Parameters.AddWithValue("@org", string.Empty);
            cmd.Parameters.AddWithValue("@proj", string.Empty);
            cmd.Parameters.AddWithValue("@repo", string.Empty);
            cmd.Parameters.AddWithValue("@adisplay", string.Empty);
            cmd.Parameters.AddWithValue("@acanon", string.Empty);
            cmd.Parameters.AddWithValue("@msg", string.Empty);
            cmd.Parameters.AddWithValue("@status", string.Empty);
            cmd.Parameters.AddWithValue("@created", string.Empty);
            cmd.Parameters.AddWithValue("@disc", string.Empty);
            cmd.Parameters.AddWithValue("@tid", string.Empty);
            cmd.Parameters.AddWithValue("@cid", string.Empty);
            cmd.Parameters.AddWithValue("@linked", DBNull.Value);
            cmd.Parameters.AddWithValue("@notif", 0);
            cmd.Parameters.AddWithValue("@read", 0);
            cmd.Parameters.AddWithValue("@rule", DBNull.Value);
            cmd.Parameters.AddWithValue("@reviewer", 0);
            foreach (var e in list)
            {
                cmd.Parameters["@eid"].Value = e.EventId;
                cmd.Parameters["@etype"].Value = (int)e.EventType;
                cmd.Parameters["@esrc"].Value = (int)e.EventSource;
                cmd.Parameters["@emean"].Value = (int)e.EventMeaning;
                cmd.Parameters["@inbox"].Value = e.InboxName;
                cmd.Parameters["@cnt"].Value = e.CollapsedCount;
                cmd.Parameters["@prid"].Value = e.PullRequestId;
                cmd.Parameters["@prtitle"].Value = e.PullRequestTitle;
                cmd.Parameters["@prurl"].Value = e.PullRequestUrl;
                cmd.Parameters["@org"].Value = e.Organization;
                cmd.Parameters["@proj"].Value = e.Project;
                cmd.Parameters["@repo"].Value = e.Repository;
                cmd.Parameters["@adisplay"].Value = e.AuthorDisplayName;
                cmd.Parameters["@acanon"].Value = e.AuthorCanonicalKey;
                cmd.Parameters["@msg"].Value = e.MessageText;
                cmd.Parameters["@status"].Value = e.Status;
                cmd.Parameters["@created"].Value = e.CreatedAtUtc.ToString("O");
                cmd.Parameters["@disc"].Value = e.DiscoveredAtUtc.ToString("O");
                cmd.Parameters["@tid"].Value = e.SourceThreadId;
                cmd.Parameters["@cid"].Value = e.SourceCommentId;
                cmd.Parameters["@linked"].Value = (object?)e.LinkedWorkItemId ?? DBNull.Value;
                cmd.Parameters["@notif"].Value = e.NotificationSent ? 1 : 0;
                cmd.Parameters["@read"].Value = e.IsRead ? 1 : 0;
                cmd.Parameters["@rule"].Value = (object?)e.MatchedRuleDescription ?? DBNull.Value;
                cmd.Parameters["@reviewer"].Value = e.IsCurrentUserReviewer ? 1 : 0;
                await NonQueryRetryAsync(cmd, ct);
            }

            // For multi-item collapsed events: carry over prior read state, then delete stale older rows
            foreach (var e in list.Where(x => x.CollapsedCount > 1))
            {
                await using var carryCmd = _conn.CreateCommand();
                carryCmd.Transaction = (SqliteTransaction)tx;
                carryCmd.CommandText = """
                    UPDATE events SET is_read = 1
                    WHERE event_id = @keep
                      AND EXISTS (
                        SELECT 1 FROM events
                        WHERE pull_request_id = @prId AND event_source = @src
                          AND collapsed_count > 1 AND event_id != @keep AND is_read = 1
                      )
                    """;
                carryCmd.Parameters.AddWithValue("@keep", e.EventId);
                carryCmd.Parameters.AddWithValue("@prId", e.PullRequestId);
                carryCmd.Parameters.AddWithValue("@src", (int)e.EventSource);
                await NonQueryRetryAsync(carryCmd, ct);

                await using var delCmd = _conn.CreateCommand();
                delCmd.Transaction = (SqliteTransaction)tx;
                delCmd.CommandText = """
                    DELETE FROM events
                    WHERE pull_request_id = @prId AND event_source = @src
                      AND collapsed_count > 1 AND event_id != @keep
                    """;
                delCmd.Parameters.AddWithValue("@keep", e.EventId);
                delCmd.Parameters.AddWithValue("@prId", e.PullRequestId);
                delCmd.Parameters.AddWithValue("@src", (int)e.EventSource);
                await NonQueryRetryAsync(delCmd, ct);
            }

            await tx.CommitAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<DevOpsEvent>> GetLatestEventsForInboxAsync(string inboxName, int maxCount, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT event_id, event_type, event_source, event_meaning,
                    inbox_name, collapsed_count,
                    pull_request_id, pull_request_title, pull_request_url,
                    organization, project, repository,
                    author_display_name, author_canonical_key, message_text, status,
                    created_at_utc, discovered_at_utc, source_thread_id, source_comment_id,
                    linked_work_item_id, notification_sent, is_read, matched_rule_description,
                    is_current_user_reviewer
                FROM events
                WHERE inbox_name = @inbox
                ORDER BY discovered_at_utc DESC
                LIMIT @max
                """;
            cmd.Parameters.AddWithValue("@inbox", inboxName);
            cmd.Parameters.AddWithValue("@max", maxCount);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<DevOpsEvent>();
            while (await reader.ReadAsync(ct))
                list.Add(ReadEvent(reader));
            return list;
        }
        finally { _lock.Release(); }
    }

    public async Task MarkEventsReadAsync(IEnumerable<string> eventIds, CancellationToken ct = default)
    {
        var ids = eventIds.ToList();
        if (ids.Count == 0) return;
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);
            foreach (var chunk in ids.Chunk(InClauseChunkSize))
            {
                await using var cmd = _conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                var paramNames = chunk.Select((_, i) => $"@p{i}").ToList();
                cmd.CommandText = $"UPDATE events SET is_read = 1 WHERE event_id IN ({string.Join(",", paramNames)})";
                for (int i = 0; i < chunk.Length; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                await NonQueryRetryAsync(cmd, ct);
            }
            await tx.CommitAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task MarkNotificationSentAsync(IEnumerable<string> eventIds, CancellationToken ct = default)
    {
        var ids = eventIds.ToList();
        if (ids.Count == 0) return;
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);
            foreach (var chunk in ids.Chunk(InClauseChunkSize))
            {
                await using var cmd = _conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                var paramNames = chunk.Select((_, i) => $"@p{i}").ToList();
                cmd.CommandText = $"UPDATE events SET notification_sent = 1 WHERE event_id IN ({string.Join(",", paramNames)})";
                for (int i = 0; i < chunk.Length; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                await NonQueryRetryAsync(cmd, ct);
            }
            await tx.CommitAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task MarkInboxReadAsync(string inboxName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE events SET is_read = 1 WHERE inbox_name = @name AND is_read = 0";
            cmd.Parameters.AddWithValue("@name", inboxName);
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    private static readonly JsonSerializerOptions InboxJson = DevPulse.Core.Services.SharedJsonOptions.Settings;

    public async Task ApplyInboxChangesAsync(
        IReadOnlyList<(string OldName, string NewName)> renames,
        IReadOnlyList<string> deletions,
        IReadOnlyList<InboxDefinition> newInboxes,
        CancellationToken ct = default)
    {
        var settingsValue = JsonSerializer.Serialize(newInboxes, InboxJson);
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);

            foreach (var (oldName, newName) in renames)
            {
                await using var cmd = _conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = "UPDATE events SET inbox_name = @newName WHERE inbox_name = @oldName";
                cmd.Parameters.AddWithValue("@newName", newName);
                cmd.Parameters.AddWithValue("@oldName", oldName);
                await NonQueryRetryAsync(cmd, ct);
            }

            foreach (var name in deletions)
            {
                await using var cmd = _conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = "DELETE FROM events WHERE inbox_name = @name";
                cmd.Parameters.AddWithValue("@name", name);
                await NonQueryRetryAsync(cmd, ct);
            }

            await using (var kvCmd = _conn.CreateCommand())
            {
                kvCmd.Transaction = (SqliteTransaction)tx;
                kvCmd.CommandText = "INSERT INTO kv_settings VALUES(@key,@val) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
                kvCmd.Parameters.AddWithValue("@key", "InboxDefinitions");
                kvCmd.Parameters.AddWithValue("@val", settingsValue);
                await NonQueryRetryAsync(kvCmd, ct);
            }

            await tx.CommitAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> GetUnreadCountForInboxAsync(string inboxName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(SUM(CASE WHEN collapsed_count > 1 THEN collapsed_count ELSE 1 END), 0)
                FROM events
                WHERE inbox_name = @inbox AND is_read = 0
                """;
            cmd.Parameters.AddWithValue("@inbox", inboxName);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
        finally { _lock.Release(); }
    }

    // ── Work Items ────────────────────────────────────────────────────────────

    public async Task UpsertWorkItemsAsync(IEnumerable<WorkItem> items, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);
            await using var cmd = _conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
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
            cmd.Parameters.AddWithValue("@id", 0);
            cmd.Parameters.AddWithValue("@title", string.Empty);
            cmd.Parameters.AddWithValue("@type", 0);
            cmd.Parameters.AddWithValue("@state", string.Empty);
            cmd.Parameters.AddWithValue("@col", string.Empty);
            cmd.Parameters.AddWithValue("@pri", string.Empty);
            cmd.Parameters.AddWithValue("@adisplay", string.Empty);
            cmd.Parameters.AddWithValue("@acanon", string.Empty);
            cmd.Parameters.AddWithValue("@area", string.Empty);
            cmd.Parameters.AddWithValue("@iter", string.Empty);
            cmd.Parameters.AddWithValue("@url", string.Empty);
            cmd.Parameters.AddWithValue("@linkedpr", DBNull.Value);
            cmd.Parameters.AddWithValue("@statedt", string.Empty);
            cmd.Parameters.AddWithValue("@days", 0);
            cmd.Parameters.AddWithValue("@aging", 0);
            cmd.Parameters.AddWithValue("@disc", string.Empty);
            cmd.Parameters.AddWithValue("@firstseen", string.Empty);
            foreach (var workItem in items)
            {
                cmd.Parameters["@id"].Value = workItem.Id;
                cmd.Parameters["@title"].Value = workItem.Title;
                cmd.Parameters["@type"].Value = (int)workItem.Type;
                cmd.Parameters["@state"].Value = workItem.State;
                cmd.Parameters["@col"].Value = workItem.BoardColumn;
                cmd.Parameters["@pri"].Value = workItem.Priority;
                cmd.Parameters["@adisplay"].Value = workItem.AssignedToDisplayName;
                cmd.Parameters["@acanon"].Value = workItem.AssignedToCanonicalKey;
                cmd.Parameters["@area"].Value = workItem.AreaPath;
                cmd.Parameters["@iter"].Value = workItem.IterationPath;
                cmd.Parameters["@url"].Value = workItem.WorkItemUrl;
                cmd.Parameters["@linkedpr"].Value = (object?)workItem.LinkedPullRequestId ?? DBNull.Value;
                cmd.Parameters["@statedt"].Value = workItem.StateChangedAtUtc.ToString("O");
                cmd.Parameters["@days"].Value = workItem.DaysInCurrentState;
                cmd.Parameters["@aging"].Value = (int)workItem.AgingLevel;
                cmd.Parameters["@disc"].Value = workItem.DiscoveredAtUtc.ToString("O");
                cmd.Parameters["@firstseen"].Value = DateTimeOffset.UtcNow.ToString("O");
                await NonQueryRetryAsync(cmd, ct);
            }
            await tx.CommitAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<WorkItem>> GetWorkItemsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, title, item_type, state, board_column, priority,
                    assigned_to_display, assigned_to_canonical, area_path, iteration_path,
                    work_item_url, linked_pr_id, state_changed_at, days_in_state,
                    aging_level, discovered_at, first_seen_utc
                FROM work_items
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var list = new List<WorkItem>();
            while (await reader.ReadAsync(ct))
                list.Add(ReadWorkItem(reader));
            return list;
        }
        finally { _lock.Release(); }
    }

    // ── Mutes ─────────────────────────────────────────────────────────────────

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
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveMuteEntryAsync(MuteEntry entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mute_entries WHERE scope = @scope AND key = @key";
            cmd.Parameters.AddWithValue("@scope", (int)entry.Scope);
            cmd.Parameters.AddWithValue("@key", entry.DbKey);
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    private static async Task<int> NonQueryRetryAsync(SqliteCommand cmd, CancellationToken ct)
    {
        try { return await cmd.ExecuteNonQueryAsync(ct); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            Log.Warning("SQLite busy (error {Code}); retrying write once", ex.SqliteErrorCode);
            await Task.Delay(50, ct);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task PurgeExpiredMutesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM mute_entries WHERE expires_at IS NOT NULL AND expires_at <= @now";
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<MuteEntry>> GetActiveMutesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT scope, expires_at, pr_id, author_key FROM mute_entries WHERE expires_at IS NULL OR expires_at > @now";
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var ordScope = reader.GetOrdinal("scope");
            var ordExp = reader.GetOrdinal("expires_at");
            var ordPrId = reader.GetOrdinal("pr_id");
            var ordAuthorKey = reader.GetOrdinal("author_key");
            var list = new List<MuteEntry>();
            while (await reader.ReadAsync(ct))
            {
                DateTimeOffset? exp = null;
                if (!reader.IsDBNull(ordExp))
                {
                    var s = reader.GetString(ordExp);
                    if (DateTimeOffset.TryParseExact(s, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    {
                        exp = dt;
                    }
                    else
                    {
                        // Skip this entry rather than defaulting to permanent mute — a corrupt
                        // expiry must not silently promote a time-limited mute to forever.
                        Log.Warning("SqliteStateStore: unparseable mute expiry '{Value}' — dropping entry", s);
                        continue;
                    }
                }
                list.Add(new MuteEntry
                {
                    Scope = (MuteScope)reader.GetInt32(ordScope),
                    PrId = reader.IsDBNull(ordPrId) ? null : reader.GetInt32(ordPrId),
                    AuthorKey = reader.IsDBNull(ordAuthorKey) ? string.Empty : reader.GetString(ordAuthorKey),
                    ExpiresAtUtc = exp
                });
            }
            return list;
        }
        finally { _lock.Release(); }
    }

    // ── Poll State ────────────────────────────────────────────────────────────

    public async Task<DateTimeOffset?> GetLastSuccessfulPollAsync(string track, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT last_success FROM poll_state WHERE track = @track";
            cmd.Parameters.AddWithValue("@track", track);
            var val = await cmd.ExecuteScalarAsync(ct);
            if (val == null || val == DBNull.Value) return null;
            var s = (string)val;
            return DateTimeOffset.TryParseExact(s, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt : null;
        }
        finally { _lock.Release(); }
    }

    public async Task SetLastSuccessfulPollAsync(string track, DateTimeOffset ts, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO poll_state VALUES(@track, @ts) ON CONFLICT(track) DO UPDATE SET last_success=excluded.last_success";
            cmd.Parameters.AddWithValue("@track", track);
            cmd.Parameters.AddWithValue("@ts", ts.ToString("O"));
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    // ── PR Snapshots ──────────────────────────────────────────────────────────

    public async Task SavePrSnapshotAsync(int prId, string status, string votesJson, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO pr_snapshots VALUES(@id,@status,@votes) ON CONFLICT(pr_id) DO UPDATE SET status=excluded.status, votes_json=excluded.votes_json";
            cmd.Parameters.AddWithValue("@id", prId);
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@votes", votesJson);
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    public async Task SavePrSnapshotsAsync(IReadOnlyList<(int PrId, string Status, string VotesJson)> snapshots, CancellationToken ct = default)
    {
        if (snapshots.Count == 0) return;
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);
            await using var cmd = _conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "INSERT INTO pr_snapshots VALUES(@id,@status,@votes) ON CONFLICT(pr_id) DO UPDATE SET status=excluded.status, votes_json=excluded.votes_json";
            cmd.Parameters.AddWithValue("@id", 0);
            cmd.Parameters.AddWithValue("@status", string.Empty);
            cmd.Parameters.AddWithValue("@votes", string.Empty);
            foreach (var (prId, status, votesJson) in snapshots)
            {
                cmd.Parameters["@id"].Value = prId;
                cmd.Parameters["@status"].Value = status;
                cmd.Parameters["@votes"].Value = votesJson;
                await NonQueryRetryAsync(cmd, ct);
            }
            await tx.CommitAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<Dictionary<int, (string? Status, string? VotesJson)>> GetPrSnapshotsAsync(IEnumerable<int> prIds, CancellationToken ct = default)
    {
        var ids = prIds.ToList();
        if (ids.Count == 0) return [];
        await _lock.WaitAsync(ct);
        try
        {
            var result = new Dictionary<int, (string?, string?)>();
            foreach (var chunk in ids.Chunk(InClauseChunkSize))
            {
                await using var cmd = _conn.CreateCommand();
                var paramNames = chunk.Select((_, i) => $"@p{i}").ToList();
                cmd.CommandText = $"SELECT pr_id, status, votes_json FROM pr_snapshots WHERE pr_id IN ({string.Join(",", paramNames)})";
                for (int i = 0; i < chunk.Length; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", chunk[i]);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                var ordPrId = reader.GetOrdinal("pr_id");
                var ordStatus = reader.GetOrdinal("status");
                var ordVotes = reader.GetOrdinal("votes_json");
                while (await reader.ReadAsync(ct))
                    result[reader.GetInt32(ordPrId)] = (reader.GetString(ordStatus), reader.GetString(ordVotes));
            }
            return result;
        }
        finally { _lock.Release(); }
    }

    public async Task CleanStaleSnapshotsAsync(IReadOnlyList<int> activePrIds, int retainDays = 30, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            if (activePrIds.Count == 0)
            {
                // No active PRs known: fall back to date-only cleanup to avoid wiping everything
                cmd.CommandText = """
                    DELETE FROM pr_snapshots
                    WHERE pr_id NOT IN (
                        SELECT DISTINCT pull_request_id FROM events
                        WHERE discovered_at_utc >= @cutoff
                    )
                    """;
                cmd.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow.AddDays(-retainDays).ToString("O"));
            }
            else
            {
                var paramNames = activePrIds.Select((_, i) => $"@a{i}").ToList();
                cmd.CommandText = $"""
                    DELETE FROM pr_snapshots
                    WHERE pr_id NOT IN ({string.Join(",", paramNames)})
                      AND pr_id NOT IN (
                        SELECT DISTINCT pull_request_id FROM events
                        WHERE discovered_at_utc >= @cutoff
                      )
                    """;
                cmd.Parameters.AddWithValue("@cutoff", DateTimeOffset.UtcNow.AddDays(-retainDays).ToString("O"));
                for (int i = 0; i < activePrIds.Count; i++)
                    cmd.Parameters.AddWithValue($"@a{i}", activePrIds[i]);
            }
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

    // ── KV Settings ───────────────────────────────────────────────────────────

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM kv_settings WHERE key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            var val = await cmd.ExecuteScalarAsync(ct);
            return val == null || val == DBNull.Value ? null : val.ToString();
        }
        finally { _lock.Release(); }
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO kv_settings VALUES(@key,@val) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@val", value);
            await NonQueryRetryAsync(cmd, ct);
        }
        finally { _lock.Release(); }
    }

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

    // ── AI Attempts ───────────────────────────────────────────────────────────

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

    // ── Readers ───────────────────────────────────────────────────────────────

    private static DateTimeOffset? ParseStoredDate(SqliteDataReader r, string column)
    {
        var ord = r.GetOrdinal(column);
        var s = r.IsDBNull(ord) ? null : r.GetString(ord);
        if (s == null) return null;
        if (DateTimeOffset.TryParseExact(s, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        Log.Warning("SqliteStateStore: unparseable date in '{Column}': {Value}", column, s);
        return null;
    }

    private static TEnum ParseEnum<TEnum>(int raw, string column, string eventId) where TEnum : struct, Enum
    {
        if (Enum.IsDefined(typeof(TEnum), raw)) return (TEnum)(object)raw;
        Log.Warning("SqliteStateStore: invalid {Enum} value {Raw} for event {EventId} column {Column}; defaulting to 0",
            typeof(TEnum).Name, raw, eventId, column);
        return default;
    }

    private static DevOpsEvent ReadEvent(SqliteDataReader r)
    {
        var ordLinkedWi = r.GetOrdinal("linked_work_item_id");
        var ordRuleDesc = r.GetOrdinal("matched_rule_description");
        var eventId = r.GetString(r.GetOrdinal("event_id"));
        return new DevOpsEvent
        {
            EventId = eventId,
            EventType = ParseEnum<DevOpsEventType>(r.GetInt32(r.GetOrdinal("event_type")), "event_type", eventId),
            EventSource = ParseEnum<PrEventSource>(r.GetInt32(r.GetOrdinal("event_source")), "event_source", eventId),
            EventMeaning = ParseEnum<EventMeaning>(r.GetInt32(r.GetOrdinal("event_meaning")), "event_meaning", eventId),
            InboxName = r.GetString(r.GetOrdinal("inbox_name")),
            CollapsedCount = r.GetInt32(r.GetOrdinal("collapsed_count")),
            PullRequestId = r.GetInt32(r.GetOrdinal("pull_request_id")),
            PullRequestTitle = r.GetString(r.GetOrdinal("pull_request_title")),
            PullRequestUrl = r.GetString(r.GetOrdinal("pull_request_url")),
            Organization = r.GetString(r.GetOrdinal("organization")),
            Project = r.GetString(r.GetOrdinal("project")),
            Repository = r.GetString(r.GetOrdinal("repository")),
            AuthorDisplayName = r.GetString(r.GetOrdinal("author_display_name")),
            AuthorCanonicalKey = r.GetString(r.GetOrdinal("author_canonical_key")),
            MessageText = r.GetString(r.GetOrdinal("message_text")),
            Status = r.GetString(r.GetOrdinal("status")),
            CreatedAtUtc = ParseStoredDate(r, "created_at_utc") ?? DateTimeOffset.MinValue,
            DiscoveredAtUtc = ParseStoredDate(r, "discovered_at_utc") ?? DateTimeOffset.MinValue,
            SourceThreadId = r.GetString(r.GetOrdinal("source_thread_id")),
            SourceCommentId = r.GetString(r.GetOrdinal("source_comment_id")),
            LinkedWorkItemId = r.IsDBNull(ordLinkedWi) ? null : r.GetString(ordLinkedWi),
            NotificationSent = r.GetInt32(r.GetOrdinal("notification_sent")) == 1,
            IsRead = r.GetInt32(r.GetOrdinal("is_read")) == 1,
            MatchedRuleDescription = r.IsDBNull(ordRuleDesc) ? null : r.GetString(ordRuleDesc),
            IsCurrentUserReviewer = r.GetInt32(r.GetOrdinal("is_current_user_reviewer")) == 1
        };
    }

    private static WorkItem ReadWorkItem(SqliteDataReader r)
    {
        var stateChangedAt = ParseStoredDate(r, "state_changed_at") ?? DateTimeOffset.MinValue;
        var ordLinkedPr = r.GetOrdinal("linked_pr_id");
        var ordFirstSeen = r.GetOrdinal("first_seen_utc");
        var freshDays = stateChangedAt == DateTimeOffset.MinValue
            ? 0
            : Math.Max(0, (int)(DateTimeOffset.UtcNow - stateChangedAt).TotalDays);
        return new WorkItem
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            Title = r.GetString(r.GetOrdinal("title")),
            Type = (WorkItemType)r.GetInt32(r.GetOrdinal("item_type")),
            State = r.GetString(r.GetOrdinal("state")),
            BoardColumn = r.GetString(r.GetOrdinal("board_column")),
            Priority = r.GetString(r.GetOrdinal("priority")),
            AssignedToDisplayName = r.GetString(r.GetOrdinal("assigned_to_display")),
            AssignedToCanonicalKey = r.GetString(r.GetOrdinal("assigned_to_canonical")),
            AreaPath = r.GetString(r.GetOrdinal("area_path")),
            IterationPath = r.GetString(r.GetOrdinal("iteration_path")),
            WorkItemUrl = r.GetString(r.GetOrdinal("work_item_url")),
            LinkedPullRequestId = r.IsDBNull(ordLinkedPr) ? null : r.GetString(ordLinkedPr),
            StateChangedAtUtc = stateChangedAt,
            DaysInCurrentState = freshDays,
            AgingLevel = (AgingLevel)r.GetInt32(r.GetOrdinal("aging_level")),
            DiscoveredAtUtc = ParseStoredDate(r, "discovered_at") ?? DateTimeOffset.MinValue,
            FirstSeenUtc = r.IsDBNull(ordFirstSeen) ? null : ParseStoredDate(r, "first_seen_utc")
        };
    }

    Task IAiAttemptStore.RecordAttemptAsync(AiAttempt attempt, CancellationToken ct)
        => RecordAiAttemptAsync(attempt, ct);

    Task<IReadOnlyList<AiAttempt>> IAiAttemptStore.GetAttemptsForWorkItemAsync(int workItemId, CancellationToken ct)
        => GetAiAttemptsForWorkItemAsync(workItemId, ct);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        await _conn.CloseAsync();
        _conn.Dispose();
        _lock.Dispose();
    }
}
