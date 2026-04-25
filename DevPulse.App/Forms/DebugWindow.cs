using System.Diagnostics;
using System.Text;
using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class DebugWindow : Form
{
    private static readonly Color DarkBg = Color.FromArgb(20, 20, 36);
    private static readonly Color TextPrimary = Color.FromArgb(220, 220, 235);

    private readonly DebugLogService _debugLog;
    private readonly SettingsService _settings;
    private int _displayCount = 500;
    private int _errorDisplayCount = 100;

    // Each tab owns one SortableListView<T>. Held as object so the export button can
    // reach the active tab's list polymorphically via the IExportableTab adapter.
    private readonly List<IExportableTab> _exportable = new();

    private readonly Button _btnExport;

    public DebugWindow(DebugLogService debugLog, SettingsService settings)
    {
        _debugLog = debugLog;
        _settings = settings;
        InitializeComponent();

        _btnExport = new Button
        {
            Text = "Export",
            Dock = DockStyle.Bottom,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 78),
            ForeColor = TextPrimary,
        };
        _btnExport.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 110);
        _btnExport.Click += BtnExport_Click;
        Controls.Add(_btnExport);

        _tabs.TabPages.Add(BuildPollStatusTab());
        _tabs.TabPages.Add(BuildEventLogTab());
        _tabs.TabPages.Add(BuildRuleTracesTab());
        _tabs.TabPages.Add(BuildIdentityLogTab());
        _tabs.TabPages.Add(BuildMuteLogTab());

        _ = RefreshAllAsync().ContinueWith(
            t => Log.Warning(t.Exception?.GetBaseException(), "DebugWindow initial refresh failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        try { await RefreshAllAsync(); }
        catch (Exception ex) { Log.Warning(ex, "DebugWindow refresh failed"); }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        try
        {
            int idx = _tabs.SelectedIndex;
            if (idx < 0 || idx >= _exportable.Count) return;
            var tab = _exportable[idx];
            var csv = tab.BuildCsv();

            var tempName = Path.GetTempFileName();
            var csvName = Path.ChangeExtension(tempName, ".csv");
            // GetTempFileName creates the .tmp; rename to .csv so the OS opens it with the spreadsheet handler.
            File.Move(tempName, csvName, overwrite: true);
            File.WriteAllText(csvName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Process.Start(new ProcessStartInfo(csvName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "DebugWindow export failed");
        }
    }

    // ----- Row record types -----

    private sealed record PollStatusRow(string Track, DateTimeOffset? LastSuccessUtc, DateTimeOffset? NextScheduledUtc, int ApiCalls, string LastError);
    private sealed record EventLogRow(DateTimeOffset Timestamp, string EventId, string Author, string Source, string Meaning, string Inbox, string Rule);
    private sealed record RuleTraceRow(DateTimeOffset Timestamp, string EventId, string Inbox, string RuleMatched);
    private sealed record IdentityRow(DateTimeOffset Timestamp, string EventId, string AuthorCanonical, string Source);
    private sealed record MuteRow(DateTimeOffset Timestamp, string EventId, string Error);

    // ----- Tab builders -----

    private TabPage BuildPollStatusTab()
    {
        var page = new TabPage("Poll status") { BackColor = DarkBg };
        var list = new SortableListView<PollStatusRow>
        {
            Dock = DockStyle.Fill,
            EmptyStateText = "(no poll status yet)",
        };
        list.SetColumns(new[]
        {
            new SortableListColumn<PollStatusRow> { Name = "Track", Width = 100, ValueSelector = r => r.Track },
            new SortableListColumn<PollStatusRow> { Name = "Last success", Width = 110, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.LastSuccessUtc, DisplaySelector = r => FormatTime(r.LastSuccessUtc) },
            new SortableListColumn<PollStatusRow> { Name = "Next scheduled", Width = 110, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.NextScheduledUtc, DisplaySelector = r => FormatTime(r.NextScheduledUtc) },
            new SortableListColumn<PollStatusRow> { Name = "API calls", Width = 90, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.ApiCalls },
            new SortableListColumn<PollStatusRow> { Name = "Last error", Width = 200, IsStretch = true,
                ValueSelector = r => r.LastError },
        });
        page.Controls.Add(list);
        _exportable.Add(new ExportableTab<PollStatusRow>(list));
        return page;
    }

    private TabPage BuildEventLogTab()
    {
        var page = new TabPage("Event log") { BackColor = DarkBg };
        var list = new SortableListView<EventLogRow>
        {
            Dock = DockStyle.Fill,
            EmptyStateText = "(no recent events)",
        };
        list.SetColumns(new[]
        {
            new SortableListColumn<EventLogRow> { Name = "Time", Width = 90, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.Timestamp, DisplaySelector = r => r.Timestamp.ToLocalTime().ToString("HH:mm:ss") },
            new SortableListColumn<EventLogRow> { Name = "EventId", Width = 200, ValueSelector = r => r.EventId },
            new SortableListColumn<EventLogRow> { Name = "Author", Width = 140, ValueSelector = r => r.Author },
            new SortableListColumn<EventLogRow> { Name = "Source", Width = 80, ValueSelector = r => r.Source },
            new SortableListColumn<EventLogRow> { Name = "Meaning", Width = 110, ValueSelector = r => r.Meaning },
            new SortableListColumn<EventLogRow> { Name = "Inbox", Width = 120, ValueSelector = r => r.Inbox },
            new SortableListColumn<EventLogRow> { Name = "Rule", Width = 200, IsStretch = true,
                ValueSelector = r => r.Rule },
        });
        page.Controls.Add(list);
        _exportable.Add(new ExportableTab<EventLogRow>(list));
        return page;
    }

    private TabPage BuildRuleTracesTab()
    {
        var page = new TabPage("Rule traces") { BackColor = DarkBg };
        var list = new SortableListView<RuleTraceRow>
        {
            Dock = DockStyle.Fill,
            EmptyStateText = "(no rule traces)",
        };
        list.SetColumns(new[]
        {
            new SortableListColumn<RuleTraceRow> { Name = "Time", Width = 90, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.Timestamp, DisplaySelector = r => r.Timestamp.ToLocalTime().ToString("HH:mm:ss") },
            new SortableListColumn<RuleTraceRow> { Name = "EventId", Width = 220, ValueSelector = r => r.EventId },
            new SortableListColumn<RuleTraceRow> { Name = "Inbox", Width = 140, ValueSelector = r => r.Inbox },
            new SortableListColumn<RuleTraceRow> { Name = "Rule matched", Width = 240, IsStretch = true,
                ValueSelector = r => r.RuleMatched },
        });
        page.Controls.Add(list);
        _exportable.Add(new ExportableTab<RuleTraceRow>(list));
        return page;
    }

    private TabPage BuildIdentityLogTab()
    {
        var page = new TabPage("Identity log") { BackColor = DarkBg };
        var list = new SortableListView<IdentityRow>
        {
            Dock = DockStyle.Fill,
            EmptyStateText = "(no identity entries)",
        };
        list.SetColumns(new[]
        {
            new SortableListColumn<IdentityRow> { Name = "Time", Width = 90, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.Timestamp, DisplaySelector = r => r.Timestamp.ToLocalTime().ToString("HH:mm:ss") },
            new SortableListColumn<IdentityRow> { Name = "EventId", Width = 220, ValueSelector = r => r.EventId },
            new SortableListColumn<IdentityRow> { Name = "Author canonical", Width = 240, IsStretch = true,
                ValueSelector = r => r.AuthorCanonical },
            new SortableListColumn<IdentityRow> { Name = "Source", Width = 80, ValueSelector = r => r.Source },
        });
        page.Controls.Add(list);
        _exportable.Add(new ExportableTab<IdentityRow>(list));
        return page;
    }

    private TabPage BuildMuteLogTab()
    {
        var page = new TabPage("Mute log") { BackColor = DarkBg };
        var list = new SortableListView<MuteRow>
        {
            Dock = DockStyle.Fill,
            EmptyStateText = "(no errors)",
        };
        list.SetColumns(new[]
        {
            new SortableListColumn<MuteRow> { Name = "Time", Width = 90, Alignment = ColumnAlignment.Right,
                ValueSelector = r => r.Timestamp, DisplaySelector = r => r.Timestamp.ToLocalTime().ToString("HH:mm:ss") },
            new SortableListColumn<MuteRow> { Name = "EventId", Width = 220, ValueSelector = r => r.EventId },
            new SortableListColumn<MuteRow> { Name = "Error", Width = 300, IsStretch = true,
                ValueSelector = r => r.Error },
        });
        page.Controls.Add(list);
        _exportable.Add(new ExportableTab<MuteRow>(list));
        return page;
    }

    public async Task RefreshAllAsync()
    {
        var appSettings = await _settings.GetAppSettingsAsync().ConfigureAwait(false);
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            try { Invoke(() => RefreshAllUi(appSettings)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { /* form closing */ }
            return;
        }

        RefreshAllUi(appSettings);
    }

    private void RefreshAllUi(DevPulse.Core.Models.AppSettings appSettings)
    {
        var events = _debugLog.GetRecentEvents();
        var pollStatus = _debugLog.GetPollStatus();

        _displayCount = Math.Max(10, appSettings.DebugWindowDisplayCount);
        _errorDisplayCount = Math.Min(_displayCount, 100);

        var pollList = FindList<PollStatusRow>(_tabs.TabPages[0]);
        pollList?.SetItems(pollStatus.Select(s => new PollStatusRow(
            s.Track, s.LastSuccessUtc, s.NextScheduledUtc, s.ApiCallCount, s.LastError ?? string.Empty)));

        var evtList = FindList<EventLogRow>(_tabs.TabPages[1]);
        evtList?.SetItems(events.TakeLast(_displayCount).Select(e => new EventLogRow(
            e.Timestamp, e.EventId, e.AuthorCanonicalKey, e.EventSource, e.EventMeaning, e.InboxAssigned, e.RuleMatched)));

        var traceList = FindList<RuleTraceRow>(_tabs.TabPages[2]);
        traceList?.SetItems(events.TakeLast(_displayCount).Select(e => new RuleTraceRow(
            e.Timestamp, e.EventId, e.InboxAssigned, e.RuleMatched)));

        var idList = FindList<IdentityRow>(_tabs.TabPages[3]);
        idList?.SetItems(events.TakeLast(_displayCount).Select(e => new IdentityRow(
            e.Timestamp, e.EventId, e.AuthorCanonicalKey, e.EventSource)));

        var muteList = FindList<MuteRow>(_tabs.TabPages[4]);
        muteList?.SetItems(events.Where(x => x.ErrorMessage != null).TakeLast(_errorDisplayCount).Select(e => new MuteRow(
            e.Timestamp, e.EventId, e.ErrorMessage ?? string.Empty)));
    }

    private static SortableListView<T>? FindList<T>(TabPage page)
        => page.Controls.OfType<SortableListView<T>>().FirstOrDefault();

    private static string FormatTime(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("HH:mm:ss") ?? string.Empty;

    // ----- CSV export -----

    private interface IExportableTab
    {
        string BuildCsv();
    }

    private sealed class ExportableTab<T> : IExportableTab
    {
        private readonly SortableListView<T> _list;
        public ExportableTab(SortableListView<T> list) => _list = list;

        public string BuildCsv()
        {
            var sb = new StringBuilder();
            var cols = _list.Columns;
            for (int c = 0; c < cols.Count; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(CsvEscape(cols[c].Name));
            }
            sb.Append("\r\n");

            foreach (var row in _list.VisibleItems)
            {
                for (int c = 0; c < cols.Count; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append(CsvEscape(cols[c].GetDisplay(row)));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            bool mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!mustQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
