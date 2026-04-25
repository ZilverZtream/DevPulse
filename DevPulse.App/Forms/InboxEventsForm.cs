using DevPulse.App;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class InboxEventsForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(30, 30, 46);
    private static readonly Color CardBg = Color.FromArgb(50, 50, 74);
    private static readonly Color TextPrimary = Color.FromArgb(224, 224, 240);
    private static readonly Color TextSecondary = Color.FromArgb(144, 144, 176);

    private readonly InboxViewService _viewService;
    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly string _inboxName;
    private readonly BoardForm? _boardForm;
    private readonly CancellationTokenSource _formCts = new();

    private int _loading;

    public InboxEventsForm(
        InboxViewService viewService,
        IStateStore store,
        SettingsService settings,
        string inboxName,
        BoardForm? boardForm = null)
    {
        _viewService = viewService;
        _store = store;
        _settings = settings;
        _inboxName = inboxName;
        _boardForm = boardForm;
        Disposed += (_, _) => { _formCts.Cancel(); _formCts.Dispose(); };
        InitializeComponent();
        Text = $"DevPulse — {_inboxName}";
        LoadEventsAsync().FireAndForget(nameof(LoadEventsAsync));
    }

    private async void BtnMarkAll_Click(object? sender, EventArgs e)
    {
        try { await MarkAllReadAsync(); }
        catch (Exception ex) { Log.Error(ex, "Mark all read failed"); }
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        try { await LoadEventsAsync(); }
        catch (Exception ex) { Log.Error(ex, "Inbox refresh failed"); }
    }

    private async Task LoadEventsAsync()
    {
        if (Interlocked.CompareExchange(ref _loading, 1, 0) != 0) return;
        var originalRefreshText = _btnRefresh.Text;
        try
        {
            _btnRefresh.Enabled = false;
            _btnRefresh.Text = "Loading…";
            var inboxes = await _settings.GetInboxDefinitionsAsync(_formCts.Token);
            var max = inboxes.FirstOrDefault(i => i.Name.Equals(_inboxName, StringComparison.OrdinalIgnoreCase))?.MaxItemsToRetain ?? 100;
            var events = await _viewService.GetLatestAsync(_inboxName, max, _formCts.Token);
            if (IsDisposed) return;
            try
            {
                _listView.Items.Clear();
                foreach (var evt in events)
                {
                    var icon = evt.IsCollapsed ? "⊞" : evt.IsRead ? "·" : "●";
                    var item = new ListViewItem(icon);
                    item.SubItems.Add($"#{evt.PullRequestId}");
                    var titleRunes = evt.PullRequestTitle.EnumerateRunes().Take(36).ToList();
                    var title = titleRunes.Count > 35
                        ? string.Concat(titleRunes.Take(35).Select(r => r.ToString())) + "…"
                        : evt.PullRequestTitle;
                    item.SubItems.Add(title);
                    item.SubItems.Add(evt.AuthorDisplayName);
                    string summary;
                    if (evt.IsCollapsed)
                    {
                        summary = $"{evt.CollapsedCount} events collapsed";
                    }
                    else
                    {
                        var msgRunes = evt.MessageText.EnumerateRunes().Take(41).ToList();
                        summary = msgRunes.Count > 40
                            ? string.Concat(msgRunes.Take(40).Select(r => r.ToString())) + "…"
                            : evt.MessageText;
                    }
                    item.SubItems.Add(summary);
                    item.SubItems.Add(evt.DiscoveredAtUtc.ToLocalTime().ToString("MM/dd HH:mm"));
                    item.Tag = evt;
                    item.ForeColor = evt.IsRead ? TextSecondary : TextPrimary;
                    _listView.Items.Add(item);
                }
            }
            catch (ObjectDisposedException) { return; }
        }
        finally
        {
            if (!IsDisposed)
            {
                _btnRefresh.Text = originalRefreshText;
                _btnRefresh.Enabled = true;
            }
            Volatile.Write(ref _loading, 0);
        }
    }

    private void OnDoubleClick(object? sender, MouseEventArgs e)
    {
        if (_listView.SelectedItems.Count == 0) return;
        if (_listView.SelectedItems[0].Tag is not DevOpsEvent evt) return;
        OpenPr(evt);
    }

    private void OnRightClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _listView.HitTest(e.Location);
        if (hit.Item?.Tag is not DevOpsEvent evt) return;

        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => menu.Dispose();
        menu.Items.Add("Open PR in browser", null, (_, _) => OpenPr(evt));

        if (!string.IsNullOrEmpty(evt.LinkedWorkItemId))
            menu.Items.Add("Open linked work item in browser", null, (_, _) => OpenWorkItem(evt));

        if (_boardForm != null && !string.IsNullOrEmpty(evt.LinkedWorkItemId))
            menu.Items.Add("Jump to work item on board", null, (_, _) => { _boardForm.Show(); _boardForm.BringToFront(); });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mark as read", null, async (_, _) =>
        {
            try { await _viewService.MarkReadAsync([evt.EventId], _formCts.Token); await LoadEventsAsync(); }
            catch (Exception ex) { Log.Error(ex, "Mark read failed for event {EventId}", evt.EventId); }
        });
        menu.Items.Add("Snooze PR (1h)", null, async (_, _) =>
        {
            try { await SnoozePrAsync(evt.PullRequestId, TimeSpan.FromHours(1)); }
            catch (Exception ex) { Log.Error(ex, "Snooze failed for PR #{PrId}", evt.PullRequestId); }
        });
        menu.Items.Add("Snooze PR (4h)", null, async (_, _) =>
        {
            try { await SnoozePrAsync(evt.PullRequestId, TimeSpan.FromHours(4)); }
            catch (Exception ex) { Log.Error(ex, "Snooze failed for PR #{PrId}", evt.PullRequestId); }
        });
        menu.Items.Add("Mute PR permanently", null, async (_, _) =>
        {
            try { await MutePrAsync(evt.PullRequestId); }
            catch (Exception ex) { Log.Error(ex, "Mute failed for PR #{PrId}", evt.PullRequestId); }
        });

        menu.Show(_listView, e.Location);
    }

    private static void OpenPr(DevOpsEvent evt)
    {
        if (!string.IsNullOrEmpty(evt.PullRequestUrl))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(evt.PullRequestUrl) { UseShellExecute = true });
    }

    private static void OpenWorkItem(DevOpsEvent evt)
    {
        if (string.IsNullOrEmpty(evt.LinkedWorkItemId)) return;
        if (!Uri.TryCreate(evt.PullRequestUrl, UriKind.Absolute, out var uri))
        {
            Log.Warning("OpenWorkItem: PR URL is not a valid absolute URI: {Url}", evt.PullRequestUrl);
            MessageBox.Show("Could not open work item — the PR URL is not a valid absolute URI.",
                "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var gitIdx = uri.AbsolutePath.IndexOf("/_git/", StringComparison.Ordinal);
        if (gitIdx < 0)
        {
            Log.Warning("OpenWorkItem: PR URL does not contain '/_git/' segment; unsupported ADO layout: {Url}", evt.PullRequestUrl);
            MessageBox.Show($"Could not determine work item URL from PR URL format.\n\nExpected URL to contain '/_git/'.\nGot: {evt.PullRequestUrl}",
                "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var baseUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath[..gitIdx]}";
        var url = $"{baseUrl}/_workitems/edit/{evt.LinkedWorkItemId}";
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task SnoozePrAsync(int prId, TimeSpan duration)
    {
        var entry = MuteService.CreatePrSnooze(prId, DateTimeOffset.UtcNow + duration);
        await _store.SaveMuteEntryAsync(entry, _formCts.Token);
        await LoadEventsAsync();
    }

    private async Task MutePrAsync(int prId)
    {
        var entry = MuteService.CreatePrMute(prId);
        await _store.SaveMuteEntryAsync(entry, _formCts.Token);
        await LoadEventsAsync();
    }

    private async Task MarkAllReadAsync()
    {
        await _store.MarkInboxReadAsync(_inboxName, _formCts.Token);
        await LoadEventsAsync();
    }

}
