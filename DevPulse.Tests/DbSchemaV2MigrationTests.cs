using DevPulse.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace DevPulse.Tests;

public class DbSchemaV2MigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"devpulse-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task EnsureCreatedAsync_CreatesAiAttemptsTable()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ai_attempts'";
        var name = (string?)await cmd.ExecuteScalarAsync();
        name.Should().Be("ai_attempts");
    }

    [Fact]
    public async Task EnsureCreatedAsync_AddsFirstSeenUtcToWorkItems()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(work_items)";
        await using var reader = await cmd.ExecuteReaderAsync();
        var cols = new List<string>();
        while (await reader.ReadAsync()) cols.Add(reader.GetString(1));
        cols.Should().Contain("first_seen_utc");
    }

    [Fact]
    public async Task EnsureCreatedAsync_SetsSchemaVersionToCurrent()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM db_meta WHERE key='schema_version'";
        var v = (string?)await cmd.ExecuteScalarAsync();
        v.Should().Be(DbSchema.CurrentSchemaVersion.ToString());
    }

    [Fact]
    public async Task EnsureCreatedAsync_IsIdempotent()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        await DbSchema.EnsureCreatedAsync(conn);
        await DbSchema.EnsureCreatedAsync(conn);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
