using DevPulse.App.Forms;
using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using DevPulse.Infrastructure.AzureDevOps;
using DevPulse.Infrastructure.Notifications;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Infrastructure.Security;
using Serilog;

namespace DevPulse.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SqliteStateStore _store;
    private readonly SettingsService _settings;
    private readonly DebugLogService _debugLog;
    private readonly InboxViewService _inboxView;
    private readonly WindowsFormsSynchronizationContext _uiSync = new();

    private PollingService? _prPoller;
    private WorkItemPollingService? _wiPoller;
    private EventHandler? _prPollCompleted;
    private EventHandler? _wiPollCompleted;
    private Dictionary<string, int> _lastMenuCounts = [];
    private string[] _lastMenuInboxKeys = [];

    private DevPulse.App.Services.AiPipelineService? _aiPipeline;
    private List<DevPulse.Core.Interfaces.IAiProvider> _aiProviders = [];
    private IReadOnlyList<DevPulse.Core.Models.AiTemplate> _aiTemplates = [];
    private HttpClient? _aiHttpClient;

    private NotifyIcon _trayIcon = null!;
    private Icon? _baseIcon;
    private readonly Dictionary<HealthStatus, Icon> _composedIcons = new();
    private HealthStatus? _lastAppliedStatus;
    // Recent failure streak per track. PollCompleted only fires on success today, so streaks
    // currently reset on success and cannot increment from this side without a failure event.
    private int _recentPrFails;
    private int _recentWiFails;
    private const int FailingThreshold = 3;
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(2);
    private BoardForm? _boardForm;
    private DebugWindow? _debugWindow;
    private SettingsForm? _settingsForm;

    private bool _needsInitRetry;
    private bool _exitCleanupStarted;
    private int _initAttempts;
    private const int MaxInitAttempts = 3;

    public TrayApplicationContext(SqliteStateStore store)
    {
        _store = store;
        _settings = new SettingsService(store);
        _debugLog = new DebugLogService();
        _inboxView = new InboxViewService(store);

        BuildTrayIcon();
        Application.ApplicationExit += OnApplicationExit;

        _uiSync.Post(_ => RunBackground(InitializeAsync, "initialize"), null);
    }

    private async Task InitializeAsync()
    {
        _initAttempts++;
        await _settings.SeedDefaultsIfNeededAsync();

        var appSettings = await _settings.GetAppSettingsAsync();
        var patResult = SecretStore.TryLoadPat();

        if (!IsConfigured(appSettings, patResult))
        {
            if (_initAttempts >= MaxInitAttempts)
            {
                Log.Warning("InitializeAsync: reached max retry attempts ({Max}); pausing auto-retry until next manual Settings save", MaxInitAttempts);
                return;
            }
            _needsInitRetry = true;
            // First-run wizard takes priority when the user has never completed onboarding AND
            // there's no PAT yet. After it returns we recurse — the wizard saves settings, so the
            // next IsConfigured check should pass. If the user cancels we fall back to Settings.
            if (!appSettings.HasCompletedFirstRun && !patResult.IsOk)
                _uiSync.Post(_ => ShowFirstRunWizard(), null);
            else
                _uiSync.Post(_ => ShowSettings(), null);
            return;
        }

        if (!Uri.TryCreate(appSettings.OrganizationUrl, UriKind.Absolute, out _))
        {
            Log.Warning("InitializeAsync: OrganizationUrl is not a valid absolute URI — opening settings");
            if (_initAttempts >= MaxInitAttempts) return;
            _needsInitRetry = true;
            _uiSync.Post(_ => ShowSettings(), null);
            return;
        }

        var httpClient = CreateHttpClient(appSettings.OrganizationUrl, patResult.Value!);
        var notifications = new WindowsToastNotificationService();

        var adoClient = new DevPulse.Infrastructure.AzureDevOps.AzureDevOpsClient(
            httpClient, appSettings.OrganizationUrl, appSettings.Project, appSettings.RepositoryFilter);
        var wiClient = new DevPulse.Infrastructure.AzureDevOps.WorkItemClient(
            httpClient, appSettings.OrganizationUrl, appSettings.Project);

        if (string.IsNullOrWhiteSpace(appSettings.CurrentUserCanonicalKey) ||
            string.IsNullOrWhiteSpace(appSettings.CurrentUserDisplayName))
        {
            try
            {
                var me = await adoClient.GetAuthenticatedUserAsync();
                if (me != null)
                {
                    var changed = false;
                    if (string.IsNullOrEmpty(appSettings.CurrentUserCanonicalKey) && !string.IsNullOrEmpty(me.UniqueName))
                    {
                        appSettings.CurrentUserCanonicalKey = me.UniqueName;
                        changed = true;
                    }
                    if (string.IsNullOrEmpty(appSettings.CurrentUserDisplayName) && !string.IsNullOrEmpty(me.DisplayName))
                    {
                        appSettings.CurrentUserDisplayName = me.DisplayName;
                        changed = true;
                    }
                    if (changed)
                    {
                        await _settings.SaveAppSettingsAsync(appSettings);
                        Log.Information("Auto-detected current user: key={Key} display={Display}",
                            appSettings.CurrentUserCanonicalKey, appSettings.CurrentUserDisplayName);
                    }
                }
                else
                {
                    Log.Warning("Auto-detect returned no email; set CurrentUserCanonicalKey in Settings to enable NeedsMyAttention routing");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to auto-detect current user identity");
            }
        }

        _prPoller = new PollingService(adoClient, _store, notifications, _settings, _debugLog);
        _wiPoller = new WorkItemPollingService(wiClient, _store, _settings, _debugLog);

        _prPollCompleted = (_, _) =>
        {
            _recentPrFails = 0;
            _uiSync.Post(_ =>
            {
                _boardForm?.RefreshErrorBanner();
                RunBackground(RefreshTrayAsync, "refresh-tray");
            }, null);
        };
        _wiPollCompleted = (_, _) => _uiSync.Post(_ =>
        {
            _recentWiFails = 0;
            if (_boardForm?.Visible == true) RunBackground(() => _boardForm.LoadAsync(), "board-load");
            if (_wiPoller!.LastPollFailed && _boardForm != null) _boardForm.ShowStaleBanner = true;
            _boardForm?.RefreshErrorBanner();
            RunBackground(RefreshTrayAsync, "refresh-tray-wi");
        }, null);
        _prPoller.PollCompleted += _prPollCompleted;
        _wiPoller.PollCompleted += _wiPollCompleted;

        await RefreshTrayAsync();

        _prPoller.Start(appSettings.PrPollingIntervalMinutes);
        _wiPoller.Start(appSettings.WorkItemPollingIntervalMinutes);

        if (appSettings.RefreshOnStartup)
        {
            RunBackground(_prPoller.RefreshNowAsync, "initial-pr-refresh");
            RunBackground(_wiPoller.RefreshNowAsync, "initial-wi-refresh");
        }

        await InitializeAiPipelineAsync(appSettings);
    }

    private async Task InitializeAiPipelineAsync(DevPulse.Core.Models.AppSettings appSettings)
    {
        try
        {
            var profiles = await _settings.GetAiProviderProfilesAsync();
            var claudeProfile = profiles.FirstOrDefault(p => p.ProviderId == "claude-cli");
            var openRouterProfile = profiles.FirstOrDefault(p => p.ProviderId == "openrouter");

            var providers = new List<DevPulse.Core.Interfaces.IAiProvider>();

            if (claudeProfile?.Enabled == true && !string.IsNullOrEmpty(claudeProfile.ExecutablePath))
                providers.Add(new DevPulse.Infrastructure.Ai.ClaudeCliProvider(claudeProfile.ExecutablePath));

            if (openRouterProfile?.Enabled == true)
            {
                _aiHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                providers.Add(new DevPulse.Infrastructure.Ai.OpenRouterProvider(
                    _aiHttpClient,
                    () => DevPulse.Infrastructure.Security.SecretStore.TryLoadSecret("openrouter") is { IsOk: true, Value: var k } ? k : null));
            }

            var templates = await new DevPulse.App.Services.SettingsAiTemplateStore(_settings).GetTemplatesAsync();
            var writer = new DevPulse.Infrastructure.Ai.FilesystemSpecWriter();
            _aiPipeline = new DevPulse.App.Services.AiPipelineService(
                providers,
                new DevPulse.App.Services.SettingsAiTemplateStore(_settings),
                writer,
                _store,
                appSettings.AiOutputRootPath,
                appSettings.Project,
                async id => (await _store.GetWorkItemsAsync()).FirstOrDefault(w => w.Id == id));

            _aiProviders = providers;
            _aiTemplates = templates;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "AI pipeline initialization failed — feature disabled for this session");
        }
    }

    private void BuildTrayIcon()
    {
        _baseIcon = CreateIcon();
        _trayIcon = new NotifyIcon
        {
            Icon = GetComposedIcon(HealthStatus.Initializing),
            Text = "DevPulse — starting…",
            Visible = true
        };
        _lastAppliedStatus = HealthStatus.Initializing;
        _trayIcon.DoubleClick += (_, _) => ShowBoard();
        _trayIcon.ContextMenuStrip = BuildMinimalMenu();
    }

    private Icon GetComposedIcon(HealthStatus status)
    {
        if (_composedIcons.TryGetValue(status, out var cached)) return cached;
        var composed = TrayIconBuilder.Compose(_baseIcon!, status);
        _composedIcons[status] = composed;
        return composed;
    }

    private ContextMenuStrip BuildMinimalMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());
        return menu;
    }

    private async Task RefreshTrayAsync()
    {
        var inboxes = await _settings.GetInboxDefinitionsAsync();
        var appSettings = await _settings.GetAppSettingsAsync();
        var counts = new Dictionary<string, int>();
        foreach (var inbox in inboxes)
            counts[inbox.Name] = await _store.GetUnreadCountForInboxAsync(inbox.Name);

        // If the inbox set is unchanged, update labels in-place to avoid menu-flicker / dispose race.
        // Only do a full rebuild when inboxes are added/removed/renamed.
        var currentKeys = inboxes.OrderBy(i => i.Order).Select(i => i.Name).ToArray();
        var structuralChange = !currentKeys.SequenceEqual(_lastMenuInboxKeys);
        bool countsChanged = counts.Count != _lastMenuCounts.Count ||
                             counts.Any(kv => !_lastMenuCounts.TryGetValue(kv.Key, out var prev) || prev != kv.Value);

        if (structuralChange)
        {
            _lastMenuCounts = counts;
            _lastMenuInboxKeys = currentKeys;
            RebuildMenu(inboxes, counts, appSettings);
        }
        else if (countsChanged)
        {
            _lastMenuCounts = counts;
            TrayMenuBuilder.UpdateUnreadCounts(_trayIcon?.ContextMenuStrip, counts);
        }

        var systemInboxName = inboxes.FirstOrDefault(i => i.IsSystemInbox)?.Name ?? "Needs My Attention";
        var nma = counts.GetValueOrDefault(systemInboxName);

        var prLast = await _store.GetLastSuccessfulPollAsync("prs");
        var wiLast = await _store.GetLastSuccessfulPollAsync("workitems");
        DateTimeOffset? mostRecent = (prLast, wiLast) switch
        {
            (not null, not null) => prLast > wiLast ? prLast : wiLast,
            (not null, null) => prLast,
            (null, not null) => wiLast,
            _ => null
        };

        var status = DetermineHealth(prLast, wiLast);
        ApplyTrayIconAndText(status, nma, systemInboxName, mostRecent);
    }

    private HealthStatus DetermineHealth(DateTimeOffset? prLastSuccess, DateTimeOffset? wiLastSuccess)
    {
        if (_prPoller == null || _wiPoller == null)
            return HealthStatus.Initializing;

        // AuthRequired beats Failing — both render red but the Text summary differs.
        var prAuth = _prPoller.LastErrorRequiresUserAction && _prPoller.LastErrorKind == PollErrorKind.AuthRequired;
        var wiAuth = _wiPoller.LastErrorRequiresUserAction && _wiPoller.LastErrorKind == PollErrorKind.AuthRequired;
        if (prAuth || wiAuth) return HealthStatus.AuthRequired;

        if (_prPoller.LastErrorRequiresUserAction || _wiPoller.LastErrorRequiresUserAction)
            return HealthStatus.Failing;

        if (_recentPrFails >= FailingThreshold || _recentWiFails >= FailingThreshold)
            return HealthStatus.Failing;

        // Both tracks must be stale to flip the icon yellow — a single recent success keeps it green.
        var now = DateTimeOffset.UtcNow;
        var prStale = !prLastSuccess.HasValue || (now - prLastSuccess.Value) > StaleThreshold;
        var wiStale = !wiLastSuccess.HasValue || (now - wiLastSuccess.Value) > StaleThreshold;
        if (prStale && wiStale) return HealthStatus.Stale;

        if (_recentPrFails > 0 || _recentWiFails > 0) return HealthStatus.Stale;

        return HealthStatus.Healthy;
    }

    private void ApplyTrayIconAndText(HealthStatus status, int unreadCount, string systemInboxName, DateTimeOffset? mostRecentSuccess)
    {
        if (_trayIcon == null) return;
        if (status != _lastAppliedStatus)
        {
            _trayIcon.Icon = GetComposedIcon(status);
            _lastAppliedStatus = status;
        }
        var summary = BuildTrayText(status, unreadCount, systemInboxName, mostRecentSuccess);
        // NotifyIcon.Text is hard-capped at 127 chars by Windows shell.
        if (summary.Length > 127) summary = summary[..127];
        _trayIcon.Text = summary;
    }

    private static string BuildTrayText(HealthStatus status, int unreadCount, string systemInboxName, DateTimeOffset? lastSuccess)
    {
        var unreadFragment = unreadCount > 0
            ? $"{unreadCount} unread in {systemInboxName}"
            : "No attention needed";
        var lastFragment = lastSuccess.HasValue
            ? $"last poll {FormatAge(DateTimeOffset.UtcNow - lastSuccess.Value)} ago"
            : "no successful poll yet";

        return status switch
        {
            HealthStatus.AuthRequired => "DevPulse — Authentication failed",
            HealthStatus.Failing => $"DevPulse — Polling failing • {lastFragment}",
            HealthStatus.Stale => $"DevPulse — Stale • {lastFragment}",
            HealthStatus.Initializing => "DevPulse — starting…",
            _ => $"DevPulse — {unreadFragment} • {lastFragment}",
        };
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60) return $"{Math.Max(0, (int)age.TotalSeconds)}s";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}d";
    }

    private void RebuildMenu(
        IReadOnlyList<InboxDefinition> inboxes,
        Dictionary<string, int> counts,
        AppSettings appSettings)
    {
        var builder = new TrayMenuBuilder();
        var menu = builder.Build(
            inboxes, counts,
            refreshPrs: () => RunBackground(() => _prPoller?.RefreshNowAsync() ?? Task.CompletedTask, "refresh-prs"),
            refreshBoard: () => RunBackground(() => _wiPoller?.RefreshNowAsync() ?? Task.CompletedTask, "refresh-board"),
            openInbox: name => ShowInbox(name),
            openBoard: ShowBoard,
            openMuted: ShowMuted,
            openSettings: ShowSettings,
            openDebug: ShowDebug,
            orgUrl: appSettings.OrganizationUrl,
            exit: () => Application.Exit());

        if (_trayIcon != null)
        {
            var old = _trayIcon.ContextMenuStrip;
            _trayIcon.ContextMenuStrip = menu;
            if (old != null)
            {
                // If menu is currently visible (user right-clicked), defer disposal until it closes
                // to avoid killing a visible strip mid-interaction.
                if (old.Visible)
                    old.Closed += (_, _) => old.Dispose();
                else
                    old.Dispose();
            }
        }
    }

    private void RunBackground(Func<Task> op, string name)
        => op().ContinueWith(t =>
        {
            // Wrap the continuation body defensively — if Serilog is mid-disposal on shutdown,
            // a logging call can throw and the continuation's exception would go unobserved.
            try
            {
                if (t.IsFaulted)
                    Log.Error(t.Exception?.GetBaseException(), "Background op '{Op}' failed", name);
                else if (t.IsCanceled)
                    Log.Debug("Background op '{Op}' cancelled", name);
            }
            catch { /* logger unavailable during shutdown */ }
        }, CancellationToken.None, TaskContinuationOptions.NotOnRanToCompletion, TaskScheduler.Default);

    private void ShowInbox(string name)
    {
        var form = new InboxEventsForm(_inboxView, _store, _settings, name, _boardForm);
        form.Show();
    }

    private void ShowBoard()
    {
        if (_boardForm == null || _boardForm.IsDisposed)
        {
            _boardForm = new BoardForm(_store, _settings);
            if (_aiPipeline != null)
                _boardForm.AttachAi(_aiPipeline, _aiProviders, _aiTemplates);
            _boardForm.ErrorStateProvider = BuildBoardErrorState;
            _boardForm.OpenSettingsAction = ShowSettings;
            _boardForm.FormClosed += (_, _) => _boardForm = null;
        }
        _boardForm.Show();
        _boardForm.BringToFront();
        RunBackground(() => _boardForm.LoadAsync(), "board-load");
    }

    private void ShowDebug()
    {
        if (_debugWindow == null || _debugWindow.IsDisposed)
        {
            _debugWindow = new DebugWindow(_debugLog, _settings);
            _debugWindow.FormClosed += (_, _) => _debugWindow = null;
        }
        _debugWindow.Show();
        _debugWindow.BringToFront();
    }

    private void ShowFirstRunWizard()
    {
        using var wizard = new FirstRunForm(_settings);
        var result = wizard.ShowDialog();
        if (result == DialogResult.OK)
        {
            // Wizard saved settings + PAT; reset attempt counter so the retry goes through fresh.
            _needsInitRetry = false;
            _initAttempts = 0;
            _uiSync.Post(_ => RunBackground(InitializeAsync, "post-firstrun-initialize"), null);
        }
        else
        {
            // User cancelled the wizard — fall back to the Settings dialog so they can configure
            // manually. The existing _needsInitRetry path then re-runs init when they save.
            ShowSettings();
        }
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings);
            _settingsForm.FormClosed += (_, _) =>
            {
                _settingsForm = null;
                if (_needsInitRetry && _prPoller == null)
                {
                    _needsInitRetry = false;
                    // User saved settings → reset attempt counter and retry init
                    _initAttempts = 0;
                    _uiSync.Post(_ => RunBackground(InitializeAsync, "retry-initialize"), null);
                }
            };
        }
        _settingsForm.Show();
        _settingsForm.BringToFront();
    }

    private void ShowMuted()
    {
        MessageBox.Show("Muted PRs view — coming in next iteration.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool IsConfigured(AppSettings s, PatLoadResult patResult)
        => !string.IsNullOrEmpty(s.OrganizationUrl) && !string.IsNullOrEmpty(s.Project) && patResult.IsOk;

    private static System.Net.Http.HttpClient CreateHttpClient(string orgUrl, string pat)
    {
        var client = new System.Net.Http.HttpClient(new AzureDevOpsAuthHandler(orgUrl, pat));
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var br = new SolidBrush(Color.FromArgb(91, 155, 213));
        g.FillEllipse(br, 1, 1, 14, 14);
        using var textBr = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("D", font, textBr, new RectangleF(0, 0, 16, 16), sf);
        var hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        if (_exitCleanupStarted) return;
        _exitCleanupStarted = true;
        UnsubscribePollHandlers();
        try
        {
            // Run the async cleanup on a thread-pool thread so awaits don't try to resume
            // on the (shutting-down) WinForms sync context — that's the deadlock pattern.
            Task.Run(DisposeAllAsync).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) { Log.Error(ex, "ApplicationExit cleanup failed"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            foreach (var icon in _composedIcons.Values)
            {
                try { icon.Dispose(); } catch { /* Icon.Dispose calls DestroyIcon — best-effort */ }
            }
            _composedIcons.Clear();
            _baseIcon?.Dispose();
            _aiHttpClient?.Dispose();
            if (!_exitCleanupStarted)
            {
                UnsubscribePollHandlers();
                try { Task.Run(DisposeAllAsync).Wait(TimeSpan.FromSeconds(1)); }
                catch (Exception ex) { Log.Error(ex, "Dispose cleanup failed"); }
            }
        }
        base.Dispose(disposing);
    }

    private void UnsubscribePollHandlers()
    {
        if (_prPoller != null && _prPollCompleted != null) _prPoller.PollCompleted -= _prPollCompleted;
        if (_wiPoller != null && _wiPollCompleted != null) _wiPoller.PollCompleted -= _wiPollCompleted;
    }

    private async Task DisposeAllAsync()
    {
        if (_prPoller != null) await _prPoller.DisposeAsync();
        if (_wiPoller != null) await _wiPoller.DisposeAsync();
        await _store.DisposeAsync();
    }

    private BoardErrorState BuildBoardErrorState()
    {
        var pr = _prPoller;
        var wi = _wiPoller;
        var requires = (pr?.LastErrorRequiresUserAction ?? false) || (wi?.LastErrorRequiresUserAction ?? false);
        var reason = pr?.LastErrorReason ?? wi?.LastErrorReason;
        var kind = (pr?.LastErrorKind ?? PollErrorKind.Unknown) != PollErrorKind.Unknown
            ? pr!.LastErrorKind
            : (wi?.LastErrorKind ?? PollErrorKind.Unknown);
        return new BoardErrorState(requires, reason, kind);
    }
}
