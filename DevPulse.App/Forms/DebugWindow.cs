using DevPulse.Core.Services;

namespace DevPulse.App.Forms;

public sealed class DebugWindow : Form
{
    private static readonly Color DarkBg = Color.FromArgb(20, 20, 36);
    private static readonly Color TextPrimary = Color.FromArgb(220, 220, 235);

    private readonly DebugLogService _debugLog;
    private TabControl _tabs = null!;

    public DebugWindow(DebugLogService debugLog)
    {
        _debugLog = debugLog;
        BuildUi();
        RefreshAll();
    }

    private void BuildUi()
    {
        Text = "DevPulse — Debug / Audit";
        Size = new Size(900, 650);
        BackColor = DarkBg;
        ForeColor = TextPrimary;
        Font = new Font("Consolas", 9f);
        StartPosition = FormStartPosition.CenterScreen;

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.FlatButtons
        };

        _tabs.TabPages.Add(BuildPollStatusTab());
        _tabs.TabPages.Add(BuildEventLogTab());
        _tabs.TabPages.Add(BuildRuleTracesTab());
        _tabs.TabPages.Add(BuildIdentityLogTab());
        _tabs.TabPages.Add(BuildMuteLogTab());

        var btnRefresh = new Button
        {
            Text = "Refresh",
            Dock = DockStyle.Bottom,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 78),
            ForeColor = TextPrimary,
            FlatAppearance = { BorderColor = Color.FromArgb(80, 80, 110) }
        };
        btnRefresh.Click += (_, _) => RefreshAll();

        Controls.Add(_tabs);
        Controls.Add(btnRefresh);
    }

    private TabPage BuildPollStatusTab()
    {
        var page = new TabPage("Poll status") { BackColor = DarkBg };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            BackgroundColor = DarkBg,
            GridColor = Color.FromArgb(60, 60, 80),
            DefaultCellStyle = { BackColor = DarkBg, ForeColor = TextPrimary },
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(42, 42, 60), ForeColor = TextPrimary },
            ReadOnly = true,
            AllowUserToAddRows = false,
            BorderStyle = BorderStyle.None,
            Tag = "poll"
        };
        grid.Columns.Add("track", "Track");
        grid.Columns.Add("last", "Last success");
        grid.Columns.Add("next", "Next scheduled");
        grid.Columns.Add("calls", "API calls");
        grid.Columns.Add("error", "Last error");
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildEventLogTab()
    {
        var page = new TabPage("Event log") { BackColor = DarkBg };
        page.Controls.Add(BuildLogGrid("events", ["Time", "EventId", "Author", "Source", "Meaning", "Inbox", "Rule"]));
        return page;
    }

    private TabPage BuildRuleTracesTab()
    {
        var page = new TabPage("Rule traces") { BackColor = DarkBg };
        page.Controls.Add(BuildLogGrid("traces", ["Time", "EventId", "Inbox", "Rule matched"]));
        return page;
    }

    private TabPage BuildIdentityLogTab()
    {
        var page = new TabPage("Identity log") { BackColor = DarkBg };
        page.Controls.Add(BuildLogGrid("identity", ["Time", "EventId", "Author canonical", "Source"]));
        return page;
    }

    private TabPage BuildMuteLogTab()
    {
        var page = new TabPage("Mute log") { BackColor = DarkBg };
        page.Controls.Add(BuildLogGrid("mutes", ["Time", "EventId", "Error"]));
        return page;
    }

    private static DataGridView BuildLogGrid(string tag, string[] columns)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
            BackgroundColor = Color.FromArgb(20, 20, 36),
            GridColor = Color.FromArgb(60, 60, 80),
            DefaultCellStyle = { BackColor = Color.FromArgb(20, 20, 36), ForeColor = Color.FromArgb(220, 220, 235), Font = new Font("Consolas", 8.5f) },
            ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(36, 36, 52), ForeColor = Color.FromArgb(220, 220, 235) },
            ReadOnly = true,
            AllowUserToAddRows = false,
            BorderStyle = BorderStyle.None,
            Tag = tag
        };
        foreach (var col in columns)
            grid.Columns.Add(col.ToLower().Replace(" ", "_"), col);
        return grid;
    }

    public void RefreshAll()
    {
        if (InvokeRequired) { Invoke(RefreshAll); return; }

        var events = _debugLog.GetRecentEvents();
        var pollStatus = _debugLog.GetPollStatus();

        // Poll status tab
        var pollGrid = FindGrid(_tabs.TabPages[0], "poll");
        if (pollGrid != null)
        {
            pollGrid.Rows.Clear();
            foreach (var s in pollStatus)
                pollGrid.Rows.Add(s.Track, s.LastSuccessUtc?.ToLocalTime().ToString("HH:mm:ss"), s.NextScheduledUtc?.ToLocalTime().ToString("HH:mm:ss"), s.ApiCallCount, s.LastError ?? "");
        }

        // Event log tab
        var evtGrid = FindGrid(_tabs.TabPages[1], "events");
        if (evtGrid != null)
        {
            evtGrid.Rows.Clear();
            foreach (var e in events.TakeLast(200))
                evtGrid.Rows.Add(e.Timestamp.ToLocalTime().ToString("HH:mm:ss"), e.EventId, e.AuthorCanonicalKey, e.EventSource, e.EventMeaning, e.InboxAssigned, e.RuleMatched);
        }

        // Rule traces tab
        var traceGrid = FindGrid(_tabs.TabPages[2], "traces");
        if (traceGrid != null)
        {
            traceGrid.Rows.Clear();
            foreach (var e in events.TakeLast(200))
                traceGrid.Rows.Add(e.Timestamp.ToLocalTime().ToString("HH:mm:ss"), e.EventId, e.InboxAssigned, e.RuleMatched);
        }

        // Identity log
        var idGrid = FindGrid(_tabs.TabPages[3], "identity");
        if (idGrid != null)
        {
            idGrid.Rows.Clear();
            foreach (var e in events.TakeLast(200))
                idGrid.Rows.Add(e.Timestamp.ToLocalTime().ToString("HH:mm:ss"), e.EventId, e.AuthorCanonicalKey, e.EventSource);
        }

        // Mute log (errors)
        var muteGrid = FindGrid(_tabs.TabPages[4], "mutes");
        if (muteGrid != null)
        {
            muteGrid.Rows.Clear();
            foreach (var e in events.Where(x => x.ErrorMessage != null).TakeLast(100))
                muteGrid.Rows.Add(e.Timestamp.ToLocalTime().ToString("HH:mm:ss"), e.EventId, e.ErrorMessage);
        }
    }

    private static DataGridView? FindGrid(TabPage page, string tag)
        => page.Controls.OfType<DataGridView>().FirstOrDefault(g => g.Tag?.ToString() == tag);
}
