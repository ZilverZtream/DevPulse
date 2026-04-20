using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DevPulse.Infrastructure.Persistence;

public sealed class SqliteStateStore : IStateStore, IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly SemaphoreSlim _lock = new(1, 1);

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

    public async Task<HashSet<string>> GetExistingEventIdsAsync(IEnumerable<string> candidateIds, CancellationToken ct = default)
    {
        var ids = candidateIds.ToList();
        if (ids.Count == 0) return [];
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            var paramNames = ids.Select((_, i) => $"@p{i}").ToList();
            cmd.CommandText = $"SELECT event_id FROM events WHERE event_id IN ({string.Join(",", paramNames)})";
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", ids[i]);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var result = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(ct))
                result.Add(reader.GetString(0));
            return result;
        }
        finally { _lock.Release(); }
    }

    public async Task SaveEventsAsync(IEnumerable<DevOpsEvent> events, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var tx = await _conn.BeginTransactionAsync(ct);
            foreach (var e in events)
            {
                await using var cmd = _conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = """
                    INSERT OR IGNORE INTO events (
                        event_id, event_type, event_source, event_meaning,
                        inbox_name, is_collapsed, collapsed_count,
                        pull_request_id, pull_request_title, pull_request_url,
                        organization, project, repository,
                        author_display_name, author_canonical_key, message_text, status,
                        created_at_utc, discovered_at_utc, source_thread_id, source_comment_id,
                        linked_work_item_id, notification_sent, is_read, matched_rule_description,
                        is_current_user_reviewer
                    ) VALUES (
                        @eid, @etype, @esrc, @emean,
                        @inbox, @collapsed, @cnt,
                        @prid, @prtitle, @prurl,
                        @org, @proj, @repo,
                        @adisplay, @acanon, @msg, @status,
                        @created, @disc, @tid, @cid,
                        @linked, @notif, @read, @rule,
                        @reviewer
                    )
                    """;
                cmd.Parameters.AddWithValue("@eid", e.EventId);
                cmd.Parameters.AddWithValue("@etype", (int)e.EventType);
                cmd.Parameters.AddWithValue("@esrc", (int)e.EventSource);
                cmd.Parameters.AddWithValue("@emean", (int)e.EventMeaning);
                cmd.Parameters.AddWithValue("@inbox", e.InboxName);
                cmd.Parameters.AddWithValue("@collapsed", e.CollapsedCount > 1 ? 1 : 0);
                cmd.Parameters.AddWithValue("@cnt", e.CollapsedCount);
                cmd.Parameters.AddWithValue("@prid", e.PullRequestId);
                cmd.Parameters.AddWithValue("@prtitle", e.PullRequestTitle);
                cmd.Parameters.AddWithValue("@prurl", e.PullRequestUrl);
                cmd.Parameters.AddWithValue("@org", e.Organization);
                cmd.Parameters.AddWithValue("@proj", e.Project);
                cmd.Parameters.AddWithValue("@repo", e.Repository);
                cmd.Parameters.AddWithValue("@adisplay", e.AuthorDisplayName);
                cmd.Parameters.AddWithValue("@acanon", e.AuthorCanonicalKey);
                cmd.Parameters.AddWithValue("@msg", e.MessageText);
                cmd.Parameters.AddWithValue("@status", e.Status);
                cmd.Parameters.AddWithValue("@created", e.CreatedAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@disc", e.DiscoveredAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@tid", e.SourceThreadId);
                cmd.Parameters.AddWithValue("@cid", e.SourceCommentId);
                cmd.Parameters.AddWithValue("@linked", (object?)e.LinkedWorkItemId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notif", e.NotificationSent ? 1 : 0);
                cmd.Parameters.AddWithValue("@read", e.IsRead ? 1 : 0);
                cmd.Parameters.AddWithValue("@rule", (object?)e.MatchedRuleDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@reviewer", e.IsCurrentUserReviewer ? 1 : 0);
                await cmd.ExecuteNonQueryAsync(ct);
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
                    inbox_name, is_collapsed, collapsed_count,
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
            await using var cmd = _conn.CreateCommand();
            var paramNames = ids.Select((_, i) => $"@p{i}").ToList();
            cmd.CommandText = $"UPDATE events SET is_read = 1 WHERE event_id IN ({string.Join(",", paramNames)})";
            for (int i = 0; i < ids.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", ids[i]);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<int> GetUnreadCountForInboxAsync(string inboxName, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM events WHERE inbox_name = @inbox AND is_read = 0";
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
            foreach (var workItem in items)
            {
                await using var cmd = _conn.CreateCommand();
                cmd.Transaction = (SqliteTransaction)tx;
                cmd.CommandText = """
                    INSERT INTO work_items VALUES (
                        @id, @title, @type, @state, @col, @pri,
                        @adisplay, @acanon, @area, @iter, @url,
                        @linkedpr, @statedt, @days, @aging, @disc
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
                cmd.Parameters.AddWithValue("@id", workItem.Id);
                cmd.Parameters.AddWithValue("@title", workItem.Title);
                cmd.Parameters.AddWithValue("@type", (int)workItem.Type);
                cmd.Parameters.AddWithValue("@state", workItem.State);
                cmd.Parameters.AddWithValue("@col", workItem.BoardColumn);
                cmd.Parameters.AddWithValue("@pri", workItem.Priority);
                cmd.Parameters.AddWithValue("@adisplay", workItem.AssignedToDisplayName);
                cmd.Parameters.AddWithValue("@acanon", workItem.AssignedToCanonicalKey);
                cmd.Parameters.AddWithValue("@area", workItem.AreaPath);
                cmd.Parameters.AddWithValue("@iter", workItem.IterationPath);
                cmd.Parameters.AddWithValue("@url", workItem.WorkItemUrl);
                cmd.Parameters.AddWithValue("@linkedpr", (object?)workItem.LinkedPullRequestId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@statedt", workItem.StateChangedAtUtc.ToString("O"));
                cmd.Parameters.AddWithValue("@days", workItem.DaysInCurrentState);
                cmd.Parameters.AddWithValue("@aging", (int)workItem.AgingLevel);
                cmd.Parameters.AddWithValue("@disc", workItem.DiscoveredAtUtc.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
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
                    aging_level, discovered_at
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
            await cmd.ExecuteNonQueryAsync(ct);
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
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<MuteEntry>> GetActiveMutesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var purge = _conn.CreateCommand();
            purge.CommandText = "DELETE FROM mute_entries WHERE expires_at IS NOT NULL AND expires_at <= @now";
            purge.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
            await purge.ExecuteNonQueryAsync(ct);

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT scope, key, expires_at, pr_id, author_key FROM mute_entries";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var ordScope = reader.GetOrdinal("scope");
            var ordExp = reader.GetOrdinal("expires_at");
            var ordPrId = reader.GetOrdinal("pr_id");
            var ordAuthorKey = reader.GetOrdinal("author_key");
            var list = new List<MuteEntry>();
            while (await reader.ReadAsync(ct))
            {
                var exp = reader.IsDBNull(ordExp) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(ordExp));
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
            return val == null || val == DBNull.Value ? null : DateTimeOffset.Parse((string)val);
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
            await cmd.ExecuteNonQueryAsync(ct);
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
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public async Task<(string? Status, string? VotesJson)> GetPrSnapshotAsync(int prId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT status, votes_json FROM pr_snapshots WHERE pr_id = @id";
            cmd.Parameters.AddWithValue("@id", prId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return (null, null);
            return (reader.GetString(0), reader.GetString(1));
        }
        finally { _lock.Release(); }
    }

    public async Task CleanStaleSnapshotsAsync(int retainDays = 30, CancellationToken ct = default)
    {
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
            return val == null || val == DBNull.Value ? null : (string)val;
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
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { _lock.Release(); }
    }

    // ── Readers ───────────────────────────────────────────────────────────────

    private static DateTimeOffset? ParseStoredDate(SqliteDataReader r, string column)
    {
        var s = r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetString(r.GetOrdinal(column));
        if (s == null) return null;
        if (DateTimeOffset.TryParseExact(s, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt;
        Log.Warning("SqliteStateStore: unparseable date in '{Column}': {Value}", column, s);
        return null;
    }

    private static DevOpsEvent ReadEvent(SqliteDataReader r) => new()
    {
        EventId = r.GetString(r.GetOrdinal("event_id")),
        EventType = (DevOpsEventType)r.GetInt32(r.GetOrdinal("event_type")),
        EventSource = (PrEventSource)r.GetInt32(r.GetOrdinal("event_source")),
        EventMeaning = (EventMeaning)r.GetInt32(r.GetOrdinal("event_meaning")),
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
        LinkedWorkItemId = r.IsDBNull(r.GetOrdinal("linked_work_item_id")) ? null : r.GetString(r.GetOrdinal("linked_work_item_id")),
        NotificationSent = r.GetInt32(r.GetOrdinal("notification_sent")) == 1,
        IsRead = r.GetInt32(r.GetOrdinal("is_read")) == 1,
        MatchedRuleDescription = r.IsDBNull(r.GetOrdinal("matched_rule_description")) ? null : r.GetString(r.GetOrdinal("matched_rule_description")),
        IsCurrentUserReviewer = r.GetInt32(r.GetOrdinal("is_current_user_reviewer")) == 1
    };

    private static WorkItem ReadWorkItem(SqliteDataReader r) => new()
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
        LinkedPullRequestId = r.IsDBNull(r.GetOrdinal("linked_pr_id")) ? null : r.GetString(r.GetOrdinal("linked_pr_id")),
        StateChangedAtUtc = ParseStoredDate(r, "state_changed_at") ?? DateTimeOffset.MinValue,
        DaysInCurrentState = r.GetInt32(r.GetOrdinal("days_in_state")),
        AgingLevel = (AgingLevel)r.GetInt32(r.GetOrdinal("aging_level")),
        DiscoveredAtUtc = ParseStoredDate(r, "discovered_at") ?? DateTimeOffset.MinValue
    };

    public async ValueTask DisposeAsync()
    {
        await _conn.CloseAsync();
        _conn.Dispose();
    }
}
