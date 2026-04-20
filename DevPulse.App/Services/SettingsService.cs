using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.App.Services;

public sealed class SettingsService
{
    private readonly IKvSettings _store;
    private readonly IStateStore _renameStore;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public SettingsService(IKvSettings store, IStateStore renameStore)
    {
        _store = store;
        _renameStore = renameStore;
    }

    // ── AppSettings ───────────────────────────────────────────────────────────

    public async Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("AppSettings", ct);
        if (json == null) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings(); }
        catch (JsonException ex) { Log.Warning(ex, "AppSettings JSON corrupt; returning defaults"); return new AppSettings(); }
    }

    public async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken ct = default)
        => await _store.SetSettingAsync("AppSettings", JsonSerializer.Serialize(settings, Json), ct);

    // ── Inboxes ───────────────────────────────────────────────────────────────

    public async Task<List<InboxDefinition>> GetInboxDefinitionsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("InboxDefinitions", ct);
        if (json == null) return DefaultInboxes();
        try { return JsonSerializer.Deserialize<List<InboxDefinition>>(json, Json) ?? DefaultInboxes(); }
        catch (JsonException ex) { Log.Warning(ex, "InboxDefinitions JSON corrupt; returning defaults"); return DefaultInboxes(); }
    }

    public async Task SaveInboxDefinitionsAsync(List<InboxDefinition> inboxes, CancellationToken ct = default)
    {
        var oldInboxes = await GetInboxDefinitionsAsync(ct);
        foreach (var oldInbox in oldInboxes)
        {
            var renamed = inboxes.FirstOrDefault(n =>
                n.IsSystemInbox == oldInbox.IsSystemInbox && n.Order == oldInbox.Order && n.Name != oldInbox.Name);
            if (renamed != null)
                await _renameStore.RenameInboxAsync(oldInbox.Name, renamed.Name, ct);
        }
        await _store.SetSettingAsync("InboxDefinitions", JsonSerializer.Serialize(inboxes, Json), ct);
    }

    // ── Board Columns ─────────────────────────────────────────────────────────

    public async Task<List<BoardColumnDefinition>> GetBoardColumnsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("BoardColumns", ct);
        if (json == null) return DefaultBoardColumns();
        try { return JsonSerializer.Deserialize<List<BoardColumnDefinition>>(json, Json) ?? DefaultBoardColumns(); }
        catch (JsonException ex) { Log.Warning(ex, "BoardColumns JSON corrupt; returning defaults"); return DefaultBoardColumns(); }
    }

    public async Task SaveBoardColumnsAsync(List<BoardColumnDefinition> columns, CancellationToken ct = default)
        => await _store.SetSettingAsync("BoardColumns", JsonSerializer.Serialize(columns, Json), ct);

    // ── Keyword Packs ─────────────────────────────────────────────────────────

    public async Task<List<KeywordPack>> GetKeywordPacksAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("KeywordPacks", ct);
        if (json == null) return DefaultKeywordPacks();
        try { return JsonSerializer.Deserialize<List<KeywordPack>>(json, Json) ?? DefaultKeywordPacks(); }
        catch (JsonException ex) { Log.Warning(ex, "KeywordPacks JSON corrupt; returning defaults"); return DefaultKeywordPacks(); }
    }

    public async Task SaveKeywordPacksAsync(List<KeywordPack> packs, CancellationToken ct = default)
        => await _store.SetSettingAsync("KeywordPacks", JsonSerializer.Serialize(packs, Json), ct);

    // ── Identity Aliases ──────────────────────────────────────────────────────

    public async Task<List<IdentityAlias>> GetIdentityAliasesAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("IdentityAliases", ct);
        if (json == null) return [];
        try { return JsonSerializer.Deserialize<List<IdentityAlias>>(json, Json) ?? []; }
        catch (JsonException ex) { Log.Warning(ex, "IdentityAliases JSON corrupt; returning empty"); return []; }
    }

    public async Task SaveIdentityAliasesAsync(List<IdentityAlias> aliases, CancellationToken ct = default)
        => await _store.SetSettingAsync("IdentityAliases", JsonSerializer.Serialize(aliases, Json), ct);

    // ── Watchers ──────────────────────────────────────────────────────────────

    public async Task<List<Watcher>> GetWatchersAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("Watchers", ct);
        if (json == null) return [];
        try { return JsonSerializer.Deserialize<List<Watcher>>(json, Json) ?? []; }
        catch (JsonException ex) { Log.Warning(ex, "Watchers JSON corrupt; returning empty"); return []; }
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
            Name = "CodeRabbit",
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
