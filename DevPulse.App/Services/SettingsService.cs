using System.Globalization;
using System.Text.Json;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Services;

public sealed class SettingsService
{
    private readonly IStateStore _store;
    private static readonly JsonSerializerOptions Json = SharedJsonOptions.Settings;

    private const string NeedsAttentionId = "00000000-0000-0000-0000-000000000001";
    private const string CodeRabbitId     = "00000000-0000-0000-0000-000000000002";
    private const string MergedPrsId      = "00000000-0000-0000-0000-000000000003";
    private const string PrioritizedId    = "00000000-0000-0000-0000-000000000004";

    // Set on any JsonException during a load — TrayApplicationContext / BoardForm can read this
    // and surface a banner so the user knows their settings rolled back to defaults.
    public bool HadLoadCorruption { get; private set; }
    public string? LastCorruptionDescription { get; private set; }

    public SettingsService(IStateStore store)
    {
        _store = store;
    }

    public async Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("AppSettings", ct);
        if (json == null) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(json, Json) ?? new AppSettings(); }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("AppSettings", json, "AppSettings", ex,
                JsonSerializer.Serialize(new AppSettings(), Json), ct);
            return new AppSettings();
        }
    }

    public async Task SaveAppSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(settings.OrganizationUrl))
        {
            if (!Uri.TryCreate(settings.OrganizationUrl, UriKind.Absolute, out var orgUri))
                throw new ArgumentException("OrganizationUrl must be a valid absolute URI", nameof(settings));
            // Require HTTPS so the PAT isn't transmitted in cleartext.
            if (!string.Equals(orgUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("OrganizationUrl must use HTTPS.", nameof(settings));
            // Warn (don't block) when the host isn't a recognised ADO endpoint — on-prem ADO Server
            // uses custom hostnames, so we can't strictly allow-list.
            var host = orgUri.Host;
            if (!host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) &&
                !host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
                Log.Warning("OrganizationUrl host '{Host}' is not a recognised ADO Services endpoint; ensure this is intentional (PAT will be sent to this host)", host);
        }
        if (!string.IsNullOrWhiteSpace(settings.Project))
            WiqlPathGuard.ValidatePath(settings.Project, nameof(settings.Project));
        if (!string.IsNullOrWhiteSpace(settings.AreaPath))
            WiqlPathGuard.ValidatePath(settings.AreaPath, nameof(settings.AreaPath));
        if (!string.IsNullOrWhiteSpace(settings.IterationPath))
            WiqlPathGuard.ValidatePath(settings.IterationPath, nameof(settings.IterationPath));

        await _store.SetSettingAsync("AppSettings", JsonSerializer.Serialize(settings, Json), ct);
    }

    public Task<string?> GetRawSettingAsync(string key, CancellationToken ct = default)
        => _store.GetSettingAsync(key, ct);

    public Task SetRawSettingAsync(string key, string value, CancellationToken ct = default)
        => _store.SetSettingAsync(key, value, ct);

    public async Task<List<InboxDefinition>> GetInboxDefinitionsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("InboxDefinitions", ct);
        if (json == null) return DefaultInboxes();

        List<InboxDefinition> inboxes;
        try { inboxes = JsonSerializer.Deserialize<List<InboxDefinition>>(json, Json) ?? DefaultInboxes(); }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("InboxDefinitions", json, "Inbox rules", ex,
                JsonSerializer.Serialize(DefaultInboxes(), Json), ct);
            return DefaultInboxes();
        }

        // Legacy migration: if any entry lacks an Id, use its Name as a deterministic backfill.
        // This is an in-memory transform — persistence happens the next time SaveInboxDefinitionsAsync commits.
        foreach (var i in inboxes)
            if (string.IsNullOrEmpty(i.Id) && !string.IsNullOrEmpty(i.Name))
                i.Id = "legacy:" + i.Name;

        return inboxes;
    }

    public async Task SaveInboxDefinitionsAsync(List<InboxDefinition> inboxes, CancellationToken ct = default)
    {
        // Reject empty names — the name is used as the events.inbox_name routing key and as the
        // tray-menu label; a blank entry would produce invisible rows and blank menu items.
        if (inboxes.Any(i => string.IsNullOrWhiteSpace(i.Name)))
            throw new ArgumentException("Inbox name cannot be empty or whitespace.", nameof(inboxes));

        // Reject duplicate names — events route by inbox_name string, so two inboxes with the same
        // name would share the same events and counts would double.
        var nameConflict = inboxes
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (nameConflict != null)
            throw new ArgumentException($"Inbox name '{nameConflict.Key}' is used more than once.", nameof(inboxes));

        // Backfill missing IDs for new entries. Preserve "legacy:" prefixes here so old/new matching
        // below works — both sides carry matching legacy: prefixes from GetInboxDefinitionsAsync.
        foreach (var i in inboxes)
            if (string.IsNullOrEmpty(i.Id)) i.Id = Guid.NewGuid().ToString();

        var oldInboxes = await GetInboxDefinitionsAsync(ct);

        var dedupedById = new Dictionary<string, InboxDefinition>();
        foreach (var n in inboxes)
        {
            if (string.IsNullOrEmpty(n.Id)) continue;
            if (!dedupedById.TryAdd(n.Id, n))
                Log.Warning("SaveInboxDefinitionsAsync: duplicate inbox Id {Id} ignored (first entry wins)", n.Id);
        }

        var renames = new List<(string OldName, string NewName)>();
        var deletions = new List<string>();
        foreach (var oldInbox in oldInboxes)
        {
            if (string.IsNullOrEmpty(oldInbox.Id) || string.IsNullOrEmpty(oldInbox.Name)) continue;

            if (!dedupedById.TryGetValue(oldInbox.Id, out var match))
                deletions.Add(oldInbox.Name);
            else if (!string.Equals(match.Name, oldInbox.Name, StringComparison.Ordinal))
                renames.Add((oldInbox.Name, match.Name));
        }

        // After matching: upgrade any "legacy:" prefixed IDs to real GUIDs for persistence.
        // Events already use inbox_name for routing, so this ID change doesn't orphan anything.
        var finalById = new Dictionary<string, InboxDefinition>();
        foreach (var n in dedupedById.Values)
        {
            if (n.Id.StartsWith("legacy:", StringComparison.OrdinalIgnoreCase))
                n.Id = Guid.NewGuid().ToString();
            finalById.TryAdd(n.Id, n);
        }

        await _store.ApplyInboxChangesAsync(renames, deletions, finalById.Values.ToList(), ct);
    }

    public async Task<List<BoardColumnDefinition>> GetBoardColumnsAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("BoardColumns", ct);
        if (json == null) return DefaultBoardColumns();
        try { return JsonSerializer.Deserialize<List<BoardColumnDefinition>>(json, Json) ?? DefaultBoardColumns(); }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("BoardColumns", json, "Board columns", ex,
                JsonSerializer.Serialize(DefaultBoardColumns(), Json), ct);
            return DefaultBoardColumns();
        }
    }

    public async Task SaveBoardColumnsAsync(List<BoardColumnDefinition> columns, CancellationToken ct = default)
        => await _store.SetSettingAsync("BoardColumns", JsonSerializer.Serialize(columns, Json), ct);

    public async Task<List<KeywordPack>> GetKeywordPacksAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("KeywordPacks", ct);
        if (json == null) return DefaultKeywordPacks();
        try { return JsonSerializer.Deserialize<List<KeywordPack>>(json, Json) ?? DefaultKeywordPacks(); }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("KeywordPacks", json, "Keyword packs", ex,
                JsonSerializer.Serialize(DefaultKeywordPacks(), Json), ct);
            return DefaultKeywordPacks();
        }
    }

    public async Task SaveKeywordPacksAsync(List<KeywordPack> packs, CancellationToken ct = default)
        => await _store.SetSettingAsync("KeywordPacks", JsonSerializer.Serialize(packs, Json), ct);

    public async Task<List<IdentityAlias>> GetIdentityAliasesAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("IdentityAliases", ct);
        if (json == null) return [];
        try { return JsonSerializer.Deserialize<List<IdentityAlias>>(json, Json) ?? []; }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("IdentityAliases", json, "Identity aliases", ex,
                JsonSerializer.Serialize(new List<IdentityAlias>(), Json), ct);
            return [];
        }
    }

    public async Task SaveIdentityAliasesAsync(List<IdentityAlias> aliases, CancellationToken ct = default)
        => await _store.SetSettingAsync("IdentityAliases", JsonSerializer.Serialize(aliases, Json), ct);

    public async Task<List<Watcher>> GetWatchersAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("Watchers", ct);
        if (json == null) return [];
        try { return JsonSerializer.Deserialize<List<Watcher>>(json, Json) ?? []; }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("Watchers", json, "Watchers", ex,
                JsonSerializer.Serialize(new List<Watcher>(), Json), ct);
            return [];
        }
    }

    public async Task SaveWatchersAsync(List<Watcher> watchers, CancellationToken ct = default)
        => await _store.SetSettingAsync("Watchers", JsonSerializer.Serialize(watchers, Json), ct);

    public async Task SaveAiConfigAsync(
        List<AiProviderProfile> providers,
        List<AiTemplate> templates,
        CancellationToken ct = default)
    {
        var entries = new List<(string, string)>
        {
            ("AiProviderProfiles", JsonSerializer.Serialize(providers, Json)),
            ("AiTemplates", JsonSerializer.Serialize(templates, Json))
        };
        await _store.SetSettingsBatchAsync(entries, ct);
    }

    public async Task<List<AiProviderProfile>> GetAiProviderProfilesAsync(CancellationToken ct = default)
    {
        var json = await _store.GetSettingAsync("AiProviderProfiles", ct);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<AiProviderProfile>>(json, Json) ?? []; }
        catch (JsonException ex)
        {
            await HandleCorruptionAsync("AiProviderProfiles", json, "AI provider profiles", ex,
                JsonSerializer.Serialize(new List<AiProviderProfile>(), Json), ct);
            return [];
        }
    }

    public async Task SeedDefaultsIfNeededAsync(CancellationToken ct = default)
    {
        if (await _store.GetSettingAsync("InboxDefinitions", ct) == null)
            await SaveInboxDefinitionsAsync(DefaultInboxes(), ct);
        if (await _store.GetSettingAsync("BoardColumns", ct) == null)
            await SaveBoardColumnsAsync(DefaultBoardColumns(), ct);
        if (await _store.GetSettingAsync("KeywordPacks", ct) == null)
            await SaveKeywordPacksAsync(DefaultKeywordPacks(), ct);
        if (await _store.GetSettingAsync("AppSettings", ct) == null)
            await SaveAppSettingsAsync(new AppSettings(), ct);
    }

    // Backs up the corrupt value to a sibling KV row, resets the original key to its seeded default,
    // and records a description so the UI can warn the user that data was rolled back.
    private async Task HandleCorruptionAsync(
        string key,
        string corruptValue,
        string humanLabel,
        JsonException ex,
        string defaultJson,
        CancellationToken ct)
    {
        HadLoadCorruption = true;
        LastCorruptionDescription = $"{humanLabel} JSON could not be parsed: {ex.Message}";

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        var backupKey = $"{key}.corrupt.{timestamp}";

        Log.Warning(ex, "Settings key '{Key}' was corrupt; backing up to '{BackupKey}' and resetting to defaults", key, backupKey);

        try
        {
            await _store.SetSettingsBatchAsync(
                [(backupKey, corruptValue), (key, defaultJson)],
                ct);
        }
        catch (Exception storeEx)
        {
            // If the backup write itself fails, surface it but don't crash the load — caller still gets defaults.
            Log.Error(storeEx, "Failed to back up corrupt settings key '{Key}'; defaults still returned in memory", key);
        }
    }

    private static List<InboxDefinition> DefaultInboxes() =>
    [
        new()
        {
            Id = NeedsAttentionId,
            Name = "Needs My Attention",
            Order = 0,
            IsSystemInbox = true,
            IsEnabled = true,
            ShowNotifications = true,
            Rules = []
        },
        new()
        {
            Id = CodeRabbitId,
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
            Id = MergedPrsId,
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
            Id = PrioritizedId,
            Name = "Prioritized",
            Order = 3,
            IsEnabled = true,
            ShowNotifications = true,
            Rules = []
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
