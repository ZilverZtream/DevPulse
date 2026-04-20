using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;

namespace DevPulse.App.Forms;

public sealed class BoardForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(30, 30, 46);
    private static readonly Color ToolbarBg = Color.FromArgb(36, 36, 52);

    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly BoardViewService _boardService = new();

    private Panel _toolbar = null!;
    private Panel _boardPanel = null!;
    private Label _staleBanner = null!;
    private TextBox _searchBox = null!;
    private ComboBox _typeFilter = null!;
    private ComboBox _assigneeFilter = null!;
    private ComboBox _priorityFilter = null!;

    private bool _mineOnly, _sprintOnly, _bugsOnly, _unassignedOnly;
    private IReadOnlyList<WorkItem> _allItems = [];
    private IReadOnlyList<BoardColumnDefinition> _columns = [];

    public bool ShowStaleBanner
    {
        set
        {
            if (InvokeRequired) { Invoke(() => ShowStaleBanner = value); return; }
            _staleBanner.Visible = value;
        }
    }

    public BoardForm(IStateStore store, SettingsService settings)
    {
        _store = store;
        _settings = settings;
        InitializeComponent();
        _ = LoadAsync();
    }

    private void InitializeComponent()
    {
        Text = "DevPulse — Board";
        Size = new Size(1200, 750);
        BackColor = DarkBg;
        ForeColor = Color.FromArgb(220, 220, 235);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);

        _staleBanner = new Label
        {
            Text = "⚠  Board data may be stale — last refresh failed",
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Color.FromArgb(120, 60, 30),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
            Visible = false
        };

        _toolbar = new Panel
        {
            Height = 42,
            Dock = DockStyle.Top,
            BackColor = ToolbarBg,
            Padding = new Padding(8, 6, 8, 6)
        };

        _searchBox = new TextBox
        {
            PlaceholderText = "Filter items...",
            Width = 220,
            BackColor = Color.FromArgb(42, 42, 60),
            ForeColor = Color.FromArgb(220, 220, 235),
            BorderStyle = BorderStyle.FixedSingle
        };
        _searchBox.TextChanged += (_, _) => ApplyFilters();

        _typeFilter = DarkCombo(["All types", "Feature", "Bug", "Task", "User Story"]);
        _typeFilter.SelectedIndexChanged += (_, _) => ApplyFilters();

        _assigneeFilter = DarkCombo(["All assignees"]);
        _assigneeFilter.SelectedIndexChanged += (_, _) => ApplyFilters();

        _priorityFilter = DarkCombo(["All priorities", "P1", "P2", "P3"]);
        _priorityFilter.SelectedIndexChanged += (_, _) => ApplyFilters();

        _toolbar.Controls.Add(_searchBox);
        int x = _searchBox.Right + 8;
        _typeFilter.Left = x; _toolbar.Controls.Add(_typeFilter); x = _typeFilter.Right + 8;
        _assigneeFilter.Left = x; _toolbar.Controls.Add(_assigneeFilter); x = _assigneeFilter.Right + 8;
        _priorityFilter.Left = x; _toolbar.Controls.Add(_priorityFilter); x = _priorityFilter.Right + 16;

        // Toggle buttons
        x = AddToggle(_toolbar, "Mine only", x, () => { _mineOnly = !_mineOnly; ApplyFilters(); });
        x = AddToggle(_toolbar, "Current sprint", x, () => { _sprintOnly = !_sprintOnly; ApplyFilters(); });
        x = AddToggle(_toolbar, "Bugs only", x, () => { _bugsOnly = !_bugsOnly; ApplyFilters(); });
        x = AddToggle(_toolbar, "Unassigned only", x, () => { _unassignedOnly = !_unassignedOnly; ApplyFilters(); });

        var btnRefresh = DarkButton("⟳ Refresh");
        btnRefresh.Left = x + 8;
        btnRefresh.Click += async (_, _) => await LoadAsync();
        _toolbar.Controls.Add(btnRefresh);

        _boardPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DarkBg,
            AutoScroll = true,
            Padding = new Padding(8)
        };

        Controls.Add(_boardPanel);
        Controls.Add(_toolbar);
        Controls.Add(_staleBanner);
    }

    public async Task LoadAsync()
    {
        _allItems = await _store.GetWorkItemsAsync();
        _columns = await _settings.GetBoardColumnsAsync();

        // Populate assignee dropdown
        var assignees = _allItems
            .Where(i => !string.IsNullOrEmpty(i.AssignedToDisplayName))
            .Select(i => i.AssignedToDisplayName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (InvokeRequired) { Invoke(LoadAsync); return; }

        _assigneeFilter.Items.Clear();
        _assigneeFilter.Items.Add("All assignees");
        foreach (var a in assignees) _assigneeFilter.Items.Add(a);
        if (_assigneeFilter.SelectedIndex < 0) _assigneeFilter.SelectedIndex = 0;

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (InvokeRequired) { Invoke(ApplyFilters); return; }

        var settings = _settings.GetAppSettingsAsync().GetAwaiter().GetResult();
        var textFilter = _searchBox.Text;
        var typeStr = _typeFilter.SelectedItem?.ToString();
        var assigneeStr = _assigneeFilter.SelectedItem?.ToString();
        var priorityStr = _priorityFilter.SelectedItem?.ToString();

        var filtered = _boardService.ApplyFilters(
            _allItems,
            settings.CurrentUserCanonicalKey,
            settings.IterationPath,
            _mineOnly, _sprintOnly, _bugsOnly, _unassignedOnly,
            textFilter);

        // Additional dropdown filters
        if (typeStr is not null and not "All types")
            filtered = filtered.Where(i => i.Type.ToString().Equals(typeStr.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)).ToList();
        if (assigneeStr is not null and not "All assignees")
            filtered = filtered.Where(i => i.AssignedToDisplayName == assigneeStr).ToList();
        if (priorityStr is not null and not "All priorities")
            filtered = filtered.Where(i => $"P{i.Priority}" == priorityStr).ToList();

        RenderBoard(filtered);
    }

    private void RenderBoard(IReadOnlyList<WorkItem> filteredItems)
    {
        _boardPanel.SuspendLayout();
        _boardPanel.Controls.Clear();

        var grouped = _boardService.GroupByColumn(_allItems, _columns);
        int x = 8;
        int colIndex = 0;

        foreach (var col in _columns.OrderBy(c => c.Order))
        {
            grouped.TryGetValue(col.Name, out var colItems);
            colItems ??= [];

            var colFilteredItems = filteredItems.Where(i => i.BoardColumn == col.Name).ToList();

            var panel = new BoardColumnPanel(col.Name, colIndex)
            {
                Height = _boardPanel.Height - 20,
                Left = x,
                Top = 4,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom
            };
            panel.SetItems(colItems, colFilteredItems);
            _boardPanel.Controls.Add(panel);
            x += panel.Width + 8;
            colIndex++;
        }

        _boardPanel.ResumeLayout(true);
    }

    private static ComboBox DarkCombo(string[] items)
    {
        var cb = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 130,
            BackColor = Color.FromArgb(42, 42, 60),
            ForeColor = Color.FromArgb(220, 220, 235),
            FlatStyle = FlatStyle.Flat
        };
        cb.Items.AddRange(items);
        cb.SelectedIndex = 0;
        return cb;
    }

    private static int AddToggle(Panel parent, string text, int x, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Left = x,
            Height = 26,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(50, 50, 78),
            ForeColor = Color.FromArgb(200, 200, 220),
            Padding = new Padding(8, 0, 8, 0),
            FlatAppearance = { BorderColor = Color.FromArgb(80, 80, 110) }
        };
        btn.Click += (_, _) =>
        {
            onClick();
            btn.BackColor = btn.BackColor == Color.FromArgb(50, 50, 78)
                ? Color.FromArgb(80, 80, 140)
                : Color.FromArgb(50, 50, 78);
        };
        parent.Controls.Add(btn);
        return btn.Right + 6;
    }

    private static Button DarkButton(string text) => new()
    {
        Text = text,
        Height = 26,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(50, 50, 78),
        ForeColor = Color.FromArgb(200, 200, 220),
        Padding = new Padding(8, 0, 8, 0),
        FlatAppearance = { BorderColor = Color.FromArgb(80, 80, 110) }
    };
}
