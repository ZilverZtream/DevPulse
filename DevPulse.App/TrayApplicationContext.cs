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
    private Dictionary<string, int> _lastMenuCounts = [];

    private NotifyIcon _trayIcon = null!;
    private BoardForm? _boardForm;
    private DebugWindow? _debugWindow;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(SqliteStateStore store)
    {
        _store = store;
        _settings = new SettingsService(store, store);
        _debugLog = new DebugLogService();
        _inboxView = new InboxViewService(store);

        _uiSync.Post(_ => RunBackground(InitializeAsync, "initialize"), null);
    }

    private async Task InitializeAsync()
    {
        await _settings.SeedDefaultsIfNeededAsync();

        var appSettings = await _settings.GetAppSettingsAsync();
        var patResult = SecretStore.TryLoadPat();

        if (!IsConfigured(appSettings, patResult))
        {
            ShowSettings();
            return;
        }

        if (!Uri.TryCreate(appSettings.OrganizationUrl, UriKind.Absolute, out _))
        {
            Log.Warning("InitializeAsync: OrganizationUrl is not a valid absolute URI — opening settings");
            ShowSettings();
            return;
        }

        var httpClient = CreateHttpClient(appSettings.OrganizationUrl, patResult.Value!);
        var notifications = new WindowsToastNotificationService();

        var adoClient = new DevPulse.Infrastructure.AzureDevOps.AzureDevOpsClient(
            httpClient, appSettings.OrganizationUrl, appSettings.Project, appSettings.RepositoryFilter);
        var wiClient = new DevPulse.Infrastructure.AzureDevOps.WorkItemClient(
            httpClient, appSettings.OrganizationUrl, appSettings.Project);

        _prPoller = new PollingService(adoClient, _store, notifications, _settings, _debugLog);
        _wiPoller = new WorkItemPollingService(wiClient, _store, _settings, _debugLog);

        _prPoller.PollCompleted += (_, _) => _uiSync.Post(_ => RunBackground(RefreshTrayAsync, "refresh-tray"), null);
        _wiPoller.PollCompleted += (_, _) => _uiSync.Post(_ =>
        {
            if (_boardForm?.Visible == true) RunBackground(() => _boardForm.LoadAsync(), "board-load");
            if (_wiPoller.LastPollFailed && _boardForm != null) _boardForm.ShowStaleBanner = true;
        }, null);

        BuildTrayIcon();
        await RefreshTrayAsync();

        _prPoller.Start(appSettings.PrPollingIntervalMinutes);
        _wiPoller.Start(appSettings.WorkItemPollingIntervalMinutes);

        if (appSettings.RefreshOnStartup)
        {
            RunBackground(_prPoller.RefreshNowAsync, "initial-pr-refresh");
            RunBackground(_wiPoller.RefreshNowAsync, "initial-wi-refresh");
        }
    }

    private void BuildTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "DevPulse",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowBoard();
    }

    private async Task RefreshTrayAsync()
    {
        var inboxes = await _settings.GetInboxDefinitionsAsync();
        var appSettings = await _settings.GetAppSettingsAsync();
        var counts = new Dictionary<string, int>();
        foreach (var inbox in inboxes)
            counts[inbox.Name] = await _store.GetUnreadCountForInboxAsync(inbox.Name);

        bool changed = counts.Count != _lastMenuCounts.Count ||
                       counts.Any(kv => !_lastMenuCounts.TryGetValue(kv.Key, out var prev) || prev != kv.Value);
        if (changed)
        {
            _lastMenuCounts = counts;
            RebuildMenu(inboxes, counts, appSettings);
        }

        var systemInboxName = inboxes.FirstOrDefault(i => i.IsSystemInbox)?.Name ?? "Needs My Attention";
        var nma = counts.GetValueOrDefault(systemInboxName);
        var text = nma > 0 ? $"DevPulse — {systemInboxName}: {nma}" : "DevPulse — No attention needed";
        if (_trayIcon != null)
            _trayIcon.Text = string.Concat(text.EnumerateRunes().Take(63).Select(r => r.ToString()));
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
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.ContextMenuStrip = menu;
        }
    }

    private void RunBackground(Func<Task> op, string name)
        => op().ContinueWith(t => Log.Error(t.Exception?.GetBaseException(), "Background op '{Op}' failed", name),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

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
            _debugWindow = new DebugWindow(_debugLog);
            _debugWindow.FormClosed += (_, _) => _debugWindow = null;
        }
        _debugWindow.Show();
        _debugWindow.BringToFront();
    }

    private void ShowSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _prPoller?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10));
            _wiPoller?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10));
            _store.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
        base.Dispose(disposing);
    }
}
