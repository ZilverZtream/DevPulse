using Microsoft.Data.Sqlite;

namespace DevPulse.Infrastructure.Persistence;

public static class DbSchema
{
    public static string DbPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevPulse", "devpulse.db");

    public static async Task EnsureCreatedAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS events (
                event_id TEXT PRIMARY KEY,
                event_type INTEGER NOT NULL DEFAULT 0,
                event_source INTEGER NOT NULL DEFAULT 0,
                event_meaning INTEGER NOT NULL DEFAULT 0,
                inbox_name TEXT NOT NULL DEFAULT '',
                is_collapsed INTEGER NOT NULL DEFAULT 0,
                collapsed_count INTEGER NOT NULL DEFAULT 1,
                pull_request_id INTEGER NOT NULL DEFAULT 0,
                pull_request_title TEXT NOT NULL DEFAULT '',
                pull_request_url TEXT NOT NULL DEFAULT '',
                organization TEXT NOT NULL DEFAULT '',
                project TEXT NOT NULL DEFAULT '',
                repository TEXT NOT NULL DEFAULT '',
                author_display_name TEXT NOT NULL DEFAULT '',
                author_canonical_key TEXT NOT NULL DEFAULT '',
                message_text TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT '',
                created_at_utc TEXT NOT NULL DEFAULT '',
                discovered_at_utc TEXT NOT NULL DEFAULT '',
                source_thread_id TEXT NOT NULL DEFAULT '',
                source_comment_id TEXT NOT NULL DEFAULT '',
                linked_work_item_id TEXT,
                notification_sent INTEGER NOT NULL DEFAULT 0,
                is_read INTEGER NOT NULL DEFAULT 0,
                matched_rule_description TEXT,
                is_current_user_reviewer INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_events_inbox ON events(inbox_name, discovered_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_events_pr ON events(pull_request_id);
            CREATE INDEX IF NOT EXISTS idx_events_read ON events(inbox_name, is_read);

            CREATE TABLE IF NOT EXISTS work_items (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                item_type INTEGER NOT NULL DEFAULT 0,
                state TEXT NOT NULL DEFAULT '',
                board_column TEXT NOT NULL DEFAULT '',
                priority TEXT NOT NULL DEFAULT '',
                assigned_to_display TEXT NOT NULL DEFAULT '',
                assigned_to_canonical TEXT NOT NULL DEFAULT '',
                area_path TEXT NOT NULL DEFAULT '',
                iteration_path TEXT NOT NULL DEFAULT '',
                work_item_url TEXT NOT NULL DEFAULT '',
                linked_pr_id TEXT,
                state_changed_at TEXT NOT NULL DEFAULT '',
                days_in_state INTEGER NOT NULL DEFAULT 0,
                aging_level INTEGER NOT NULL DEFAULT 0,
                discovered_at TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS mute_entries (
                scope INTEGER NOT NULL,
                key TEXT NOT NULL,
                expires_at TEXT,
                PRIMARY KEY (scope, key)
            );

            CREATE TABLE IF NOT EXISTS poll_state (
                track TEXT PRIMARY KEY,
                last_success TEXT
            );

            CREATE TABLE IF NOT EXISTS pr_snapshots (
                pr_id INTEGER PRIMARY KEY,
                status TEXT NOT NULL DEFAULT '',
                votes_json TEXT NOT NULL DEFAULT '{}'
            );

            CREATE TABLE IF NOT EXISTS kv_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL DEFAULT ''
            );
            """;
        await cmd.ExecuteNonQueryAsync();

        // Inline migration: add typed mute columns (idempotent — silent on duplicate)
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
            catch (SqliteException) { /* column already exists */ }
        }

        // Back-fill typed columns from legacy key column
        await using var backfill = conn.CreateCommand();
        backfill.CommandText = """
            UPDATE mute_entries SET pr_id = CAST(key AS INTEGER) WHERE scope = 0 AND pr_id IS NULL;
            UPDATE mute_entries SET author_key = key WHERE scope = 1 AND (author_key IS NULL OR author_key = '');
            """;
        await backfill.ExecuteNonQueryAsync();
    }
}
