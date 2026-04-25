using DevPulse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DevPulse.Tests;

public class DbSchemaV3MigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-test-v3-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EnsureCreatedAsync_SetsSchemaVersionTo3()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM db_meta WHERE key='schema_version'";
        var v = (string?)await cmd.ExecuteScalarAsync();
        v.Should().Be("3");
    }

    [Fact]
    public async Task EnsureCreatedAsync_CreatesV3Indexes()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        var indexes = await GetIndexNamesAsync(conn, "events");
        indexes.Should().Contain("idx_events_inbox_discovered");
        indexes.Should().Contain("idx_events_source_meaning");
    }

    [Fact]
    public async Task EnsureCreatedAsync_IsIdempotent_AcrossMigrations()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);
        await DbSchema.EnsureCreatedAsync(conn);
        await DbSchema.EnsureCreatedAsync(conn);

        var indexes = await GetIndexNamesAsync(conn, "events");
        indexes.Should().Contain("idx_events_inbox_discovered");
        indexes.Should().Contain("idx_events_source_meaning");
    }

    [Fact]
    public async Task EnsureCreatedAsync_UpgradesFromV2ToV3()
    {
        // Simulate an existing v2 database: build the schema, then force the version meta back to 2.
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        // Drop the v3 indexes and roll back the recorded version to v2.
        await using (var rollback = conn.CreateCommand())
        {
            rollback.CommandText = """
                DROP INDEX IF EXISTS idx_events_inbox_discovered;
                DROP INDEX IF EXISTS idx_events_source_meaning;
                UPDATE db_meta SET value = '2' WHERE key = 'schema_version';
                """;
            await rollback.ExecuteNonQueryAsync();
        }

        // Re-run migration; should bring version back up to 3 and recreate indexes.
        await DbSchema.EnsureCreatedAsync(conn);

        await using (var ver = conn.CreateCommand())
        {
            ver.CommandText = "SELECT value FROM db_meta WHERE key='schema_version'";
            var v = (string?)await ver.ExecuteScalarAsync();
            v.Should().Be("3");
        }

        var indexes = await GetIndexNamesAsync(conn, "events");
        indexes.Should().Contain("idx_events_inbox_discovered");
        indexes.Should().Contain("idx_events_source_meaning");
    }

    private static async Task<List<string>> GetIndexNamesAsync(SqliteConnection conn, string table)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name=@t";
        cmd.Parameters.AddWithValue("@t", table);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
