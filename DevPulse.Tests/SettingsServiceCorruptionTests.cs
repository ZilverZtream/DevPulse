using DevPulse.App.Services;
using DevPulse.Infrastructure.Persistence;
using FluentAssertions;

namespace DevPulse.Tests;

public class SettingsServiceCorruptionTests : IAsyncLifetime
{
    private string _dbPath = "";
    private SqliteStateStore _store = null!;
    private SettingsService _settings = null!;

    public async Task InitializeAsync()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"corrupt-settings-{Guid.NewGuid():N}.db");
        _store = new SqliteStateStore(_dbPath);
        await _store.InitializeAsync();
        _settings = new SettingsService(_store);
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        try { File.Delete(_dbPath); } catch { }
    }

    [Fact]
    public async Task GetInboxDefinitionsAsync_CorruptJson_ReturnsDefaultsAndBacksUpAndFlagsCorruption()
    {
        // Seed garbage directly into the KV store under InboxDefinitions.
        await _settings.SetRawSettingAsync("InboxDefinitions", "{not json at all}");

        var inboxes = await _settings.GetInboxDefinitionsAsync();

        // 1. Defaults returned (4 seeded inboxes).
        inboxes.Should().HaveCount(4);
        inboxes.Select(i => i.Name).Should().Contain(["Needs My Attention", "CodeRabbit", "Merged PRs", "Prioritized"]);

        // 2. Flag is set so the UI can warn the user.
        _settings.HadLoadCorruption.Should().BeTrue();
        _settings.LastCorruptionDescription.Should().NotBeNull();
        _settings.LastCorruptionDescription!.Should().Contain("Inbox rules");

        // 3. Backup row exists with the original corrupt value, keyed `{originalKey}.corrupt.{timestamp}`.
        var backupKey = await FindBackupKeyAsync("InboxDefinitions");
        backupKey.Should().NotBeNull("a backup of the corrupt value should have been written");
        var backupValue = await _settings.GetRawSettingAsync(backupKey!);
        backupValue.Should().Be("{not json at all}");

        // 4. The original key is now valid JSON containing the seeded defaults — re-loading shouldn't re-trip corruption.
        var freshSettings = new SettingsService(_store);
        var second = await freshSettings.GetInboxDefinitionsAsync();
        second.Should().HaveCount(4);
        freshSettings.HadLoadCorruption.Should().BeFalse();
    }

    [Fact]
    public async Task GetAppSettingsAsync_CorruptJson_ReturnsDefaultsAndFlags()
    {
        await _settings.SetRawSettingAsync("AppSettings", "this is not json");

        var settings = await _settings.GetAppSettingsAsync();

        settings.Should().NotBeNull();
        _settings.HadLoadCorruption.Should().BeTrue();
        _settings.LastCorruptionDescription.Should().Contain("AppSettings");

        var backupKey = await FindBackupKeyAsync("AppSettings");
        backupKey.Should().NotBeNull();
    }

    [Fact]
    public async Task GetBoardColumnsAsync_CorruptJson_ReturnsDefaultsAndFlags()
    {
        await _settings.SetRawSettingAsync("BoardColumns", "[{");

        var columns = await _settings.GetBoardColumnsAsync();

        columns.Should().NotBeEmpty();
        _settings.HadLoadCorruption.Should().BeTrue();
        var backupKey = await FindBackupKeyAsync("BoardColumns");
        backupKey.Should().NotBeNull();
    }

    [Fact]
    public async Task GetWatchersAsync_CorruptJson_ReturnsEmptyAndFlags()
    {
        await _settings.SetRawSettingAsync("Watchers", "garbage[");

        var watchers = await _settings.GetWatchersAsync();

        watchers.Should().BeEmpty();
        _settings.HadLoadCorruption.Should().BeTrue();
        (await FindBackupKeyAsync("Watchers")).Should().NotBeNull();
    }

    [Fact]
    public async Task GetInboxDefinitionsAsync_ValidJson_DoesNotFlagCorruption()
    {
        // Write a valid (empty list) value — load should succeed cleanly.
        await _settings.SetRawSettingAsync("InboxDefinitions", "[]");

        var inboxes = await _settings.GetInboxDefinitionsAsync();

        inboxes.Should().BeEmpty();
        _settings.HadLoadCorruption.Should().BeFalse();
        _settings.LastCorruptionDescription.Should().BeNull();
    }

    // Walks the KV store looking for a backup row created by HandleCorruptionAsync.
    // We don't have a "list keys" API, so we probe by inspecting the SQLite DB directly via the connection
    // string. Simpler alternative: the implementation uses keys like `{key}.corrupt.{utc-timestamp}` —
    // we test the 1-second window and then a 5-second window covering test latency.
    private async Task<string?> FindBackupKeyAsync(string originalKey)
    {
        // Probe a generous window of recent UTC timestamps to find any backup row written during the test.
        // Format: yyyyMMddTHHmmssfffZ. We can't reverse-search, so instead we use a SQL query through the
        // raw store API — but IStateStore doesn't expose it. Use Microsoft.Data.Sqlite directly.
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key FROM kv_settings WHERE key LIKE $prefix ORDER BY key DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$prefix", $"{originalKey}.corrupt.%");
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }
}
