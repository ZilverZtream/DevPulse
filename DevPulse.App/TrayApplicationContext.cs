using DevPulse.App.Forms;
using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Services;
using DevPulse.Infrastructure.Notifications;
using DevPulse.Infrastructure.Persistence;
using DevPulse.Infrastructure.Security;

namespace DevPulse.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SqliteStateStore _store;
    private readonly SettingsService _settings;
    private readonly DebugLogService _debugLog;
    private readonly InboxViewService _inboxView;
    private readonly BoardViewService _boardView;

    private PollingService? _prPoller;
    private WorkItemPollingService? _wiPoller;

    private NotifyIcon _trayIcon = null!;
    private BoardForm? _boardForm;
    private DebugWindow? _debugWindow;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(SqliteStateStore store)
    {
        _store = store;
        _settings = new SettingsService(store);
        _debugLog = new DebugLogService();
        _inboxView = new InboxViewService(store);
        _boardView = new BoardViewService();

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await _settings.SeedDefaultsIfNeededAsync();

        var appSettings = await _settings.GetAppSettingsAsync();
        var pat = SecretStore.LoadPat();

        if (!IsConfigured(appSettings, pat))
        {
            ShowSettings();
            return;
        }

        var httpClient = CreateHttpClient(pat!);
        var notifications = new WindowsToastNotificationService();

        var adoClient = new DevPulse.Infrastructure.AzureDevOps.AzureDevOpsClient(
            httpClient, appSettings.OrganizationUrl, appSettings.Project, appSettings.RepositoryFilter);
        var wiClient = new DevPulse.Infrastructure.AzureDevOps.WorkItemClient(
            httpClient, appSettings.OrganizationUrl, appSettings.Project);

        _prPoller = new PollingService(adoClient, _store, notifications, _settings, _debugLog);
        _wiPoller = new WorkItemPollingService(wiClient, _store, _settings, _debugLog);

        _prPoller.PollCompleted += async (_, _) => await RefreshTrayAsync();
        _wiPoller.PollCompleted += async (_, _) =>
        {
            if (_boardForm?.Visible == true) await _boardForm.LoadAsync();
            if (_wiPoller.LastPollFailed && _boardForm != null) _boardForm.ShowStaleBanner = true;
        };

        BuildTrayIcon();
        await RefreshTrayAsync();

        _prPoller.Start(appSettings.PrPollingIntervalMinutes);
        _wiPoller.Start(appSettings.WorkItemPollingIntervalMinutes);

        if (appSettings.RefreshOnStartup)
        {
            _ = _prPoller.RefreshNowAsync();
            _ = _wiPoller.RefreshNowAsync();
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
        _ = RebuildMenuAsync();
    }

    private async Task RefreshTrayAsync()
    {
        await RebuildMenuAsync();
        var nma = await _store.GetUnreadCountForInboxAsync("Needs My Attention");
        _trayIcon.Text = nma > 0
            ? $"DevPulse — Needs My Attention: {nma}"
            : "DevPulse — No attention needed";
    }

    private async Task RebuildMenuAsync()
    {
        var inboxes = await _settings.GetInboxDefinitionsAsync();
        var appSettings = await _settings.GetAppSettingsAsync();
        var counts = new Dictionary<string, int>();

        foreach (var inbox in inboxes)
            counts[inbox.Name] = await _store.GetUnreadCountForInboxAsync(inbox.Name);

        var builder = new TrayMenuBuilder();
        var menu = builder.Build(
            inboxes, counts,
            refreshPrs: () => _ = _prPoller?.RefreshNowAsync(),
            refreshBoard: () => _ = _wiPoller?.RefreshNowAsync(),
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
        _ = _boardForm.LoadAsync();
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

    private static bool IsConfigured(DevPulse.Core.Models.AppSettings s, string? pat)
        => !string.IsNullOrEmpty(s.OrganizationUrl) && !string.IsNullOrEmpty(s.Project) && pat != null;

    private static System.Net.Http.HttpClient CreateHttpClient(string pat)
    {
        var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}")));
        return client;
    }

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
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _prPoller?.Dispose();
            _wiPoller?.Dispose();
        }
        base.Dispose(disposing);
    }
}
