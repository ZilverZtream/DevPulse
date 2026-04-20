using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Persistence;

namespace DevPulse.App.Services;

public sealed class SettingsService
{
    private readonly SqliteStateStore _store;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public SettingsService(SqliteStateStore store) => _store = store;

    // ── AppSettings ───────────────────────────────────────────────────────────

    public async Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("AppSettings", ct);
        return json == null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings();
    }

    public async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken ct = default)
        => await _store.SetSettingAsync("AppSettings", JsonSerializer.Serialize(settings, Json), ct);

    // ── Inboxes ───────────────────────────────────────────────────────────────

    public async Task<List<InboxDefinition>> GetInboxDefinitionsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("InboxDefinitions", ct);
        return json == null ? DefaultInboxes() : JsonSerializer.Deserialize<List<InboxDefinition>>(json, Json) ?? DefaultInboxes();
    }

    public async Task SaveInboxDefinitionsAsync(List<InboxDefinition> inboxes, CancellationToken ct = default)
        => await _store.SetSettingAsync("InboxDefinitions", JsonSerializer.Serialize(inboxes, Json), ct);

    // ── Board Columns ─────────────────────────────────────────────────────────

    public async Task<List<BoardColumnDefinition>> GetBoardColumnsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("BoardColumns", ct);
        return json == null ? DefaultBoardColumns() : JsonSerializer.Deserialize<List<BoardColumnDefinition>>(json, Json) ?? DefaultBoardColumns();
    }

    public async Task SaveBoardColumnsAsync(List<BoardColumnDefinition> columns, CancellationToken ct = default)
        => await _store.SetSettingAsync("BoardColumns", JsonSerializer.Serialize(columns, Json), ct);

    // ── Keyword Packs ─────────────────────────────────────────────────────────

    public async Task<List<KeywordPack>> GetKeywordPacksAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("KeywordPacks", ct);
        return json == null ? DefaultKeywordPacks() : JsonSerializer.Deserialize<List<KeywordPack>>(json, Json) ?? DefaultKeywordPacks();
    }

    public async Task SaveKeywordPacksAsync(List<KeywordPack> packs, CancellationToken ct = default)
        => await _store.SetSettingAsync("KeywordPacks", JsonSerializer.Serialize(packs, Json), ct);

    // ── Identity Aliases ──────────────────────────────────────────────────────

    public async Task<List<IdentityAlias>> GetIdentityAliasesAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("IdentityAliases", ct);
        return json == null ? [] : JsonSerializer.Deserialize<List<IdentityAlias>>(json, Json) ?? [];
    }

    public async Task SaveIdentityAliasesAsync(List<IdentityAlias> aliases, CancellationToken ct = default)
        => await _store.SetSettingAsync("IdentityAliases", JsonSerializer.Serialize(aliases, Json), ct);

    // ── Watchers ──────────────────────────────────────────────────────────────

    public async Task<List<Watcher>> GetWatchersAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("Watchers", ct);
        return json == null ? [] : JsonSerializer.Deserialize<List<Watcher>>(json, Json) ?? [];
    }

    public async Task SaveWatchersAsync(List<Watcher> watchers, CancellationToken ct = default)
        => await _store.SetSettingAsync("Watchers", JsonSerializer.Serialize(watchers, Json), ct);

    // ── First-launch seed ────────────────────────────────────────────────────

    public async Task SeedDefaultsIfNeededAsync(CancellationToken ct = default)
    {
        var existing = await _store.GetSettingAsync("InboxDefinitions", ct);
        if (existing != null) return;

        await SaveInboxDefinitionsAsync(DefaultInboxes(), ct);
        await SaveBoardColumnsAsync(DefaultBoardColumns(), ct);
        await SaveKeywordPacksAsync(DefaultKeywordPacks(), ct);
    }

    // ── Defaults ──────────────────────────────────────────────────────────────

    private static List<InboxDefinition> DefaultInboxes() =>
    [
        new()
        {
            Name = "Needs My Attention",
            Order = 0,
            IsSystemInbox = true,
            IsEnabled = true,
            ShowNotifications = true,
            Rules = []
        },
        new()
        {
            Name = "Code Rabbit",
            Order = 1,
            IsEnabled = true,
            ShowNotifications = false,
            Rules =
            [
                new() { EventSourceEquals = PrEventSource.Bot }
            ]
        },
        new()
        {
            Name = "Merged PRs",
            Order = 2,
            IsEnabled = true,
            ShowNotifications = true,
            Rules =
            [
                new() { EventMeaningEquals = EventMeaning.Merged }
            ]
        },
        new()
        {
            Name = "Prioritized",
            Order = 3,
            IsEnabled = true,
            ShowNotifications = true,
            Rules = [] // fallback catch-all
        }
    ];

    private static List<BoardColumnDefinition> DefaultBoardColumns() =>
    [
        new() { Name = "Feature Request", Order = 0, MappedStates = ["New", "Proposed"], AgingDaysWarning = 2, AgingDaysStale = 6 },
        new() { Name = "Backlog",         Order = 1, MappedStates = ["Active"],           AgingDaysWarning = 2, AgingDaysStale = 6 },
        new() { Name = "Doing",           Order = 2, MappedStates = ["In Progress"],      AgingDaysWarning = 2, AgingDaysStale = 4 },
        new() { Name = "In Review",       Order = 3, MappedStates = ["Resolved", "In Review"], AgingDaysWarning = 1, AgingDaysStale = 2 },
        new() { Name = "Done",            Order = 4, MappedStates = ["Closed", "Completed"], AgingDaysWarning = 999, AgingDaysStale = 999 }
    ];

    private static List<KeywordPack> DefaultKeywordPacks() =>
    [
        new() { Name = "needs-attention", Keywords = ["needs test", "please review", "ready for QA", "please verify", "ready for test"] },
        new() { Name = "blocking",        Keywords = ["changes requested", "blocked", "waiting for author", "do not merge"] },
        new() { Name = "positive",        Keywords = ["approved", "LGTM", "looks good", "ship it"] },
        new() { Name = "hotfix",          Keywords = ["hotfix", "urgent", "critical", "production issue"] }
    ];
}
