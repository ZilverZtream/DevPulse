using DevPulse.App.Services;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.Security;

namespace DevPulse.App.Forms;

public sealed class SettingsForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(30, 30, 46);
    private static readonly Color PanelBg = Color.FromArgb(38, 38, 56);
    private static readonly Color TextPrimary = Color.FromArgb(220, 220, 235);
    private static readonly Color InputBg = Color.FromArgb(42, 42, 62);

    private readonly SettingsService _settings;
    private AppSettings _appSettings = new();

    // Connection
    private TextBox _orgUrl = null!, _project = null!, _repoFilter = null!, _currentUser = null!, _patBox = null!;
    // Polling
    private NumericUpDown _prInterval = null!, _wiInterval = null!;
    private CheckBox _refreshOnStartup = null!;
    // Identities
    private TextBox _botPatterns = null!, _poQaGroup = null!;
    private DataGridView _aliasGrid = null!;
    // Inboxes
    private ListBox _inboxList = null!;
    private TextBox _inboxRulesJson = null!;
    // Board
    private TextBox _areaPath = null!, _iterationPath = null!;
    private DataGridView _columnsGrid = null!;
    // Advanced
    private NumericUpDown _maxEvents = null!;

    public SettingsForm(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        _ = LoadSettingsAsync();
    }

    private void InitializeComponent()
    {
        Text = "DevPulse — Settings";
        Size = new Size(780, 580);
        MinimumSize = new Size(700, 500);
        BackColor = DarkBg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.FlatButtons,
            BackColor = DarkBg
        };

        tabs.TabPages.Add(BuildConnectionTab());
        tabs.TabPages.Add(BuildPollingTab());
        tabs.TabPages.Add(BuildIdentitiesTab());
        tabs.TabPages.Add(BuildInboxesTab());
        tabs.TabPages.Add(BuildBoardTab());
        tabs.TabPages.Add(BuildNotificationsTab());
        tabs.TabPages.Add(BuildAdvancedTab());

        var btnSave = new Button
        {
            Text = "Save",
            Dock = DockStyle.Bottom,
            Height = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 100, 160),
            ForeColor = Color.White,
            FlatAppearance = { BorderSize = 0 }
        };
        btnSave.Click += async (_, _) => await SaveSettingsAsync();

        Controls.Add(tabs);
        Controls.Add(btnSave);
    }

    // ── Connection Tab ─────────────────────────────────────────────────────────

    private TabPage BuildConnectionTab()
    {
        var page = new TabPage("Connection") { BackColor = DarkBg, ForeColor = TextPrimary };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = DarkBg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _orgUrl = AddRow(layout, "Organization URL:", new TextBox());
        _project = AddRow(layout, "Project:", new TextBox());
        _repoFilter = AddRow(layout, "Repository filter:", new TextBox());
        _currentUser = AddRow(layout, "Your email (canonical key):", new TextBox());

        _patBox = new TextBox { UseSystemPasswordChar = true };
        AddRow(layout, "Personal Access Token:", _patBox);

        var btnTest = new Button { Text = "Test connection", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(50, 80, 120), ForeColor = Color.White, Height = 28 };
        btnTest.Click += async (_, _) => await TestConnectionAsync();
        layout.Controls.Add(new Label(), 0, layout.RowCount);
        layout.Controls.Add(btnTest, 1, layout.RowCount - 1);

        page.Controls.Add(layout);
        return page;
    }

    // ── Polling Tab ────────────────────────────────────────────────────────────

    private TabPage BuildPollingTab()
    {
        var page = new TabPage("Polling") { BackColor = DarkBg, ForeColor = TextPrimary };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = DarkBg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _prInterval = new NumericUpDown { Minimum = 1, Maximum = 60, Value = 5 };
        AddRow(layout, "PR poll interval (minutes):", _prInterval);

        _wiInterval = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 10 };
        AddRow(layout, "Work item poll interval (minutes):", _wiInterval);

        _refreshOnStartup = new CheckBox { Text = "Refresh on startup", ForeColor = TextPrimary };
        layout.Controls.Add(new Label(), 0, layout.RowCount);
        layout.Controls.Add(_refreshOnStartup, 1, layout.RowCount - 1);

        page.Controls.Add(layout);
        return page;
    }

    // ── Identities Tab ─────────────────────────────────────────────────────────

    private TabPage BuildIdentitiesTab()
    {
        var page = new TabPage("Identities") { BackColor = DarkBg, ForeColor = TextPrimary };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = DarkBg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _botPatterns = new TextBox { Multiline = false };
        AddRow(layout, "Bot identity patterns (comma-sep):", _botPatterns);

        _poQaGroup = new TextBox { Multiline = false };
        AddRow(layout, "PO/QA group canonical keys (comma-sep):", _poQaGroup);

        _aliasGrid = new DataGridView
        {
            BackgroundColor = InputBg,
            GridColor = Color.FromArgb(60, 60, 80),
            DefaultCellStyle = { BackColor = InputBg, ForeColor = TextPrimary },
            ColumnHeadersDefaultCellStyle = { BackColor = PanelBg, ForeColor = TextPrimary },
            AllowUserToAddRows = true,
            BorderStyle = BorderStyle.None
        };
        _aliasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "canonical", HeaderText = "Canonical key", Width = 200 });
        _aliasGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "variants", HeaderText = "Variants (comma-sep)", Width = 300 });
        layout.Controls.Add(DarkLabel("Identity aliases:"), 0, layout.RowCount);
        layout.Controls.Add(_aliasGrid, 1, layout.RowCount - 1);
        layout.SetRowSpan(_aliasGrid, 1);

        page.Controls.Add(layout);
        return page;
    }

    // ── Inboxes Tab ────────────────────────────────────────────────────────────

    private TabPage BuildInboxesTab()
    {
        var page = new TabPage("Inboxes") { BackColor = DarkBg, ForeColor = TextPrimary };
        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = DarkBg, SplitterDistance = 200 };

        _inboxList = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(36, 36, 52), ForeColor = TextPrimary };
        _inboxList.SelectedIndexChanged += (_, _) => LoadInboxRules();
        split.Panel1.Controls.Add(_inboxList);

        _inboxRulesJson = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            BackColor = InputBg,
            ForeColor = TextPrimary,
            Font = new Font("Consolas", 8.5f),
            WordWrap = false
        };
        split.Panel2.Controls.Add(_inboxRulesJson);

        page.Controls.Add(split);
        return page;
    }

    // ── Board Tab ──────────────────────────────────────────────────────────────

    private TabPage BuildBoardTab()
    {
        var page = new TabPage("Board") { BackColor = DarkBg, ForeColor = TextPrimary };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = DarkBg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _areaPath = new TextBox();
        AddRow(layout, "Area path:", _areaPath);

        _iterationPath = new TextBox();
        AddRow(layout, "Iteration path (optional):", _iterationPath);

        _columnsGrid = new DataGridView
        {
            BackgroundColor = InputBg,
            GridColor = Color.FromArgb(60, 60, 80),
            DefaultCellStyle = { BackColor = InputBg, ForeColor = TextPrimary },
            ColumnHeadersDefaultCellStyle = { BackColor = PanelBg, ForeColor = TextPrimary },
            AllowUserToAddRows = true,
            BorderStyle = BorderStyle.None
        };
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Column", Width = 120 });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "states", HeaderText = "ADO States (comma-sep)", Width = 180 });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "warn", HeaderText = "Warn days", Width = 80 });
        _columnsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "stale", HeaderText = "Stale days", Width = 80 });
        layout.Controls.Add(DarkLabel("Board columns:"), 0, layout.RowCount);
        layout.Controls.Add(_columnsGrid, 1, layout.RowCount - 1);

        page.Controls.Add(layout);
        return page;
    }

    // ── Notifications Tab ──────────────────────────────────────────────────────

    private TabPage BuildNotificationsTab()
    {
        var page = new TabPage("Notifications") { BackColor = DarkBg, ForeColor = TextPrimary };
        var note = new Label
        {
            Text = "Configure per-inbox notifications on the Inboxes tab.\nNeeds My Attention notifications are always enabled.",
            Dock = DockStyle.Fill,
            ForeColor = TextPrimary,
            Padding = new Padding(16)
        };
        page.Controls.Add(note);
        return page;
    }

    // ── Advanced Tab ───────────────────────────────────────────────────────────

    private TabPage BuildAdvancedTab()
    {
        var page = new TabPage("Advanced") { BackColor = DarkBg, ForeColor = TextPrimary };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), BackColor = DarkBg };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _maxEvents = new NumericUpDown { Minimum = 10, Maximum = 1000, Value = 100 };
        AddRow(layout, "Max events retained per inbox:", _maxEvents);

        var btnExport = new Button { Text = "Export settings JSON…", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(50, 80, 120), ForeColor = Color.White, Height = 28 };
        btnExport.Click += (_, _) => ExportSettings();
        layout.Controls.Add(new Label(), 0, layout.RowCount);
        layout.Controls.Add(btnExport, 1, layout.RowCount - 1);

        page.Controls.Add(layout);
        return page;
    }

    // ── Load / Save ────────────────────────────────────────────────────────────

    private async Task LoadSettingsAsync()
    {
        _appSettings = await _settings.GetAppSettingsAsync();
        _orgUrl.Text = _appSettings.OrganizationUrl;
        _project.Text = _appSettings.Project;
        _repoFilter.Text = _appSettings.RepositoryFilter;
        _currentUser.Text = _appSettings.CurrentUserCanonicalKey;
        _patBox.Text = SecretStore.LoadPat() ?? string.Empty;
        _prInterval.Value = _appSettings.PrPollingIntervalMinutes;
        _wiInterval.Value = _appSettings.WorkItemPollingIntervalMinutes;
        _refreshOnStartup.Checked = _appSettings.RefreshOnStartup;
        _botPatterns.Text = string.Join(", ", _appSettings.BotIdentityPatterns);
        _poQaGroup.Text = string.Join(", ", _appSettings.PoQaGroupCanonicalKeys);
        _areaPath.Text = _appSettings.AreaPath;
        _iterationPath.Text = _appSettings.IterationPath;
        _maxEvents.Value = _appSettings.MaxEventsPerInbox;

        var inboxes = await _settings.GetInboxDefinitionsAsync();
        _inboxList.Items.Clear();
        foreach (var i in inboxes.OrderBy(x => x.Order))
            _inboxList.Items.Add(i.Name);

        var aliases = await _settings.GetIdentityAliasesAsync();
        _aliasGrid.Rows.Clear();
        foreach (var a in aliases)
            _aliasGrid.Rows.Add(a.CanonicalKey, string.Join(", ", a.Variants));

        var columns = await _settings.GetBoardColumnsAsync();
        _columnsGrid.Rows.Clear();
        foreach (var c in columns.OrderBy(x => x.Order))
            _columnsGrid.Rows.Add(c.Name, string.Join(", ", c.MappedStates), c.AgingDaysWarning, c.AgingDaysStale);
    }

    private async Task SaveSettingsAsync()
    {
        _appSettings.OrganizationUrl = _orgUrl.Text.Trim();
        _appSettings.Project = _project.Text.Trim();
        _appSettings.RepositoryFilter = _repoFilter.Text.Trim();
        _appSettings.CurrentUserCanonicalKey = _currentUser.Text.Trim();
        _appSettings.PrPollingIntervalMinutes = (int)_prInterval.Value;
        _appSettings.WorkItemPollingIntervalMinutes = (int)_wiInterval.Value;
        _appSettings.RefreshOnStartup = _refreshOnStartup.Checked;
        _appSettings.BotIdentityPatterns = [.. _botPatterns.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _appSettings.PoQaGroupCanonicalKeys = [.. _poQaGroup.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        _appSettings.AreaPath = _areaPath.Text.Trim();
        _appSettings.IterationPath = _iterationPath.Text.Trim();
        _appSettings.MaxEventsPerInbox = (int)_maxEvents.Value;

        if (!string.IsNullOrEmpty(_patBox.Text))
            SecretStore.SavePat(_patBox.Text);

        await _settings.SaveAppSettingsAsync(_appSettings);

        var aliases = new List<IdentityAlias>();
        foreach (DataGridViewRow row in _aliasGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var canon = row.Cells["canonical"].Value?.ToString() ?? string.Empty;
            var variants = row.Cells["variants"].Value?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(canon))
                aliases.Add(new IdentityAlias { CanonicalKey = canon, Variants = [.. variants.Split(',', StringSplitOptions.TrimEntries)] });
        }
        await _settings.SaveIdentityAliasesAsync(aliases);

        var columns = new List<BoardColumnDefinition>();
        int order = 0;
        foreach (DataGridViewRow row in _columnsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var name = row.Cells["name"].Value?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(name)) continue;
            var states = (row.Cells["states"].Value?.ToString() ?? string.Empty).Split(',', StringSplitOptions.TrimEntries).ToList();
            _ = int.TryParse(row.Cells["warn"].Value?.ToString(), out int warn);
            _ = int.TryParse(row.Cells["stale"].Value?.ToString(), out int stale);
            columns.Add(new BoardColumnDefinition { Name = name, Order = order++, MappedStates = states, AgingDaysWarning = warn, AgingDaysStale = stale });
        }
        await _settings.SaveBoardColumnsAsync(columns);

        MessageBox.Show("Settings saved.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void LoadInboxRules()
    {
        var selectedName = _inboxList.SelectedItem?.ToString();
        if (selectedName == null) return;
        _ = LoadInboxRulesAsync(selectedName);
    }

    private async Task LoadInboxRulesAsync(string inboxName)
    {
        var inboxes = await _settings.GetInboxDefinitionsAsync();
        var inbox = inboxes.FirstOrDefault(i => i.Name == inboxName);
        if (inbox == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(inbox, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        if (InvokeRequired) { Invoke(() => _inboxRulesJson.Text = json); return; }
        _inboxRulesJson.Text = json;
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var pat = _patBox.Text;
            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                    Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}")));
            var resp = await http.GetAsync($"{_orgUrl.Text.TrimEnd('/')}/_apis/projects?api-version=7.1");
            if (resp.IsSuccessStatusCode)
                MessageBox.Show("Connection successful!", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show($"Connection failed: {resp.StatusCode}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSettings()
    {
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "devpulse-settings.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var safe = System.Text.Json.JsonSerializer.Serialize(_appSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(dlg.FileName, safe);
        MessageBox.Show($"Exported to {dlg.FileName}\n(PAT redacted)", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private T AddRow<T>(TableLayoutPanel layout, string label, T control) where T : Control
    {
        DarkifyInput(control);
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(DarkLabel(label), 0, layout.RowCount);
        layout.Controls.Add(control, 1, layout.RowCount - 1);
        return control;
    }

    private static Label DarkLabel(string text) => new()
    {
        Text = text,
        ForeColor = Color.FromArgb(180, 180, 200),
        AutoSize = true,
        Padding = new Padding(0, 6, 0, 0)
    };

    private static void DarkifyInput(Control c)
    {
        c.BackColor = Color.FromArgb(42, 42, 62);
        c.ForeColor = Color.FromArgb(220, 220, 235);
    }
}
