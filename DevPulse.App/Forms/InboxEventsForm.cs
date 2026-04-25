using DevPulse.App;
using DevPulse.App.UI;
using DevPulse.Core.Enums;
using DevPulse.Core.Models;
using DevPulse.Core.Services;
using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class InboxEventsForm : Form
{
    private readonly InboxViewService _viewService;
    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly WindowBoundsService _bounds;
    private readonly string _inboxName;
    private readonly BoardForm? _boardForm;
    private readonly CancellationTokenSource _formCts = new();

    private SortableListView<DevOpsEvent> _list = null!;

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
        _bounds = new WindowBoundsService(store);
        _inboxName = inboxName;
        _boardForm = boardForm;
        Disposed += (_, _) => { _formCts.Cancel(); _formCts.Dispose(); };
        InitializeComponent();
        Text = $"DevPulse — {_inboxName}";
        Load += InboxEventsForm_Load;
        FormClosing += InboxEventsForm_FormClosing;
        LoadEventsAsync().FireAndForget(nameof(LoadEventsAsync));
    }

    private async void InboxEventsForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var record = await _bounds.LoadAsync(WindowBoundsService.InboxEventsFormKey, _formCts.Token);
            if (IsDisposed) return;
            WindowBoundsService.ApplyOnLoad(this, record);
        }
        catch (Exception ex) { Log.Warning(ex, "InboxEventsForm: bounds load failed"); }
    }

    private void InboxEventsForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var record = WindowBoundsService.CaptureBounds(this);
        if (record is null) return;
        _ = _bounds.SaveAsync(WindowBoundsService.InboxEventsFormKey, record);
    }

    // Called from Designer's InitializeComponent. The control is generic so the Designer cannot
    // wire it up declaratively — keep its construction here next to the column definitions.
    private void BuildList()
    {
        _list = new SortableListView<DevOpsEvent>
        {
            Dock = DockStyle.Fill,
            EmptyStateText = "(no unread events)",
        };

        _list.SetColumns(new[]
        {
            new SortableListColumn<DevOpsEvent>
            {
                Name = "PR",
                Width = 70,
                Alignment = ColumnAlignment.Right,
                ValueSelector = e => e.PullRequestId,
                DisplaySelector = e => $"#{e.PullRequestId}",
            },
            new SortableListColumn<DevOpsEvent>
            {
                Name = "Source",
                Width = 70,
                ValueSelector = e => e.EventSource.ToString(),
                DisplaySelector = e => FormatSource(e.EventSource),
            },
            new SortableListColumn<DevOpsEvent>
            {
                Name = "Meaning",
                Width = 110,
                ValueSelector = e => e.EventMeaning.ToString(),
                DisplaySelector = e => FormatMeaning(e),
            },
            new SortableListColumn<DevOpsEvent>
            {
                Name = "Title",
                Width = 280,
                IsStretch = true,
                ValueSelector = e => e.PullRequestTitle,
            },
            new SortableListColumn<DevOpsEvent>
            {
                Name = "Author",
                Width = 160,
                ValueSelector = e => e.AuthorDisplayName,
            },
            new SortableListColumn<DevOpsEvent>
            {
                Name = "Time",
                Width = 110,
                Alignment = ColumnAlignment.Right,
                ValueSelector = e => e.DiscoveredAtUtc,
                DisplaySelector = e => e.DiscoveredAtUtc.ToLocalTime().ToString("MM/dd HH:mm"),
            },
        });

        // Default sort: newest first.
        _list.Sort(5, SortDirection.Descending);

        _list.ItemActivated += (_, evt) => { if (evt is not null) OpenPr(evt); };
        _list.Refreshed += (_, _) => LoadEventsAsync().FireAndForget(nameof(LoadEventsAsync));
        _list.MouseDown += List_MouseDown;
    }

    private static string FormatSource(PrEventSource src) => src switch
    {
        PrEventSource.Human => "Human",
        PrEventSource.Bot => "Bot",
        _ => "—",
    };

    private static string FormatMeaning(DevOpsEvent e)
    {
        var name = e.EventMeaning switch
        {
            EventMeaning.Comment => "Comment",
            EventMeaning.Merged => "Merged",
            EventMeaning.Abandoned => "Abandoned",
            EventMeaning.VoteChanged => "Vote",
            EventMeaning.Blocked => "Blocked",
            EventMeaning.ReviewerAdded => "Reviewer",
            EventMeaning.Mention => "Mention",
            EventMeaning.VoteApproved => "Approved",
            EventMeaning.VoteApprovedWithSuggestions => "Approved+Sug",
            EventMeaning.VoteWaiting => "Waiting",
            _ => "—",
        };
        return e.IsCollapsed ? $"{name} ×{e.CollapsedCount}" : name;
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
                _list.SetItems(events);
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

    private void List_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var evt = _list.SelectedItem;
        if (evt is null) return;
        ShowContextMenu(evt, e.Location);
    }

    private void ShowContextMenu(DevOpsEvent evt, Point location)
    {
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

        menu.Show(_list, location);
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
