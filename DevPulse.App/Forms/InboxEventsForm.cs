using DevPulse.App;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using Serilog;

namespace DevPulse.App.Forms;

public sealed class InboxEventsForm : Form
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

    private ListView _listView = null!;
    private bool _loading;

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
        InitializeComponent();
        LoadEventsAsync().FireAndForget(nameof(LoadEventsAsync));
    }

    private void InitializeComponent()
    {
        Text = $"DevPulse — {_inboxName}";
        Size = new Size(820, 600);
        BackColor = DarkBg;
        ForeColor = TextPrimary;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        var toolbar = new Panel { Height = 36, Dock = DockStyle.Top, BackColor = Color.FromArgb(42, 42, 60), Padding = new Padding(6, 4, 6, 4) };

        var btnMarkAll = DarkButton("Mark all as read");
        btnMarkAll.Click += async (_, _) =>
        {
            try { await MarkAllReadAsync(); }
            catch (Exception ex) { Log.Error(ex, "Mark all read failed"); }
        };

        var btnRefresh = DarkButton("Refresh");
        btnRefresh.Click += async (_, _) =>
        {
            try { await LoadEventsAsync(); }
            catch (Exception ex) { Log.Error(ex, "Inbox refresh failed"); }
        };
        btnRefresh.Left = btnMarkAll.Right + 8;

        toolbar.Controls.AddRange([btnMarkAll, btnRefresh]);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            BackColor = DarkBg,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9f),
            BorderStyle = BorderStyle.None
        };
        _listView.Columns.Add("", 24);
        _listView.Columns.Add("PR", 60);
        _listView.Columns.Add("Title", 240);
        _listView.Columns.Add("Author", 160);
        _listView.Columns.Add("Summary", 220);
        _listView.Columns.Add("Time", 110);

        _listView.MouseDoubleClick += OnDoubleClick;
        _listView.MouseClick += OnRightClick;

        Controls.AddRange([_listView, toolbar]);
    }

    private async Task LoadEventsAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var events = await _viewService.GetLatestAsync(_inboxName, 100);
            _listView.Items.Clear();
            foreach (var evt in events)
            {
                var icon = evt.IsCollapsed ? "⊞" : evt.IsRead ? "·" : "●";
                var item = new ListViewItem(icon);
                item.SubItems.Add($"#{evt.PullRequestId}");
                item.SubItems.Add(evt.PullRequestTitle.Length > 35 ? evt.PullRequestTitle[..35] + "…" : evt.PullRequestTitle);
                item.SubItems.Add(evt.AuthorDisplayName);
                var summary = evt.IsCollapsed
                    ? $"{evt.CollapsedCount} events collapsed"
                    : evt.MessageText.Length > 40 ? evt.MessageText[..40] + "…" : evt.MessageText;
                item.SubItems.Add(summary);
                item.SubItems.Add(evt.DiscoveredAtUtc.ToLocalTime().ToString("MM/dd HH:mm"));
                item.Tag = evt;
                item.ForeColor = evt.IsRead ? TextSecondary : TextPrimary;
                _listView.Items.Add(item);
            }
        }
        finally { _loading = false; }
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
        menu.Items.Add("Open PR in browser", null, (_, _) => OpenPr(evt));

        if (!string.IsNullOrEmpty(evt.LinkedWorkItemId))
            menu.Items.Add("Open linked work item in browser", null, (_, _) => OpenWorkItem(evt));

        if (_boardForm != null && !string.IsNullOrEmpty(evt.LinkedWorkItemId))
            menu.Items.Add("Jump to work item on board", null, (_, _) => { _boardForm.Show(); _boardForm.BringToFront(); });

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mark as read", null, async (_, _) =>
        {
            try { await _viewService.MarkReadAsync([evt.EventId]); await LoadEventsAsync(); }
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
        // Navigate to ADO work item URL derived from org/project
        var url = evt.PullRequestUrl.Contains("_git")
            ? evt.PullRequestUrl.Split("_git")[0] + $"_workitems/edit/{evt.LinkedWorkItemId}"
            : string.Empty;
        if (!string.IsNullOrEmpty(url))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task SnoozePrAsync(int prId, TimeSpan duration)
    {
        var entry = MuteService.CreatePrSnooze(prId, DateTimeOffset.UtcNow + duration);
        await _store.SaveMuteEntryAsync(entry);
    }

    private async Task MutePrAsync(int prId)
    {
        var entry = MuteService.CreatePrMute(prId);
        await _store.SaveMuteEntryAsync(entry);
    }

    private async Task MarkAllReadAsync()
    {
        var events = await _viewService.GetLatestAsync(_inboxName, 1000);
        await _viewService.MarkReadAsync(events.Select(e => e.EventId));
        await LoadEventsAsync();
    }

    private static Button DarkButton(string text)
    {
        return new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 60, 90),
            ForeColor = Color.FromArgb(220, 220, 235),
            Height = 26,
            AutoSize = true,
            Padding = new Padding(8, 0, 8, 0),
            FlatAppearance = { BorderColor = Color.FromArgb(80, 80, 110) }
        };
    }
}
