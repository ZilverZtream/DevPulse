using DevPulse.App;
using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;

namespace DevPulse.App.Forms;

public sealed partial class BoardForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(30, 30, 46);
    private static readonly Color ToolbarBg = Color.FromArgb(36, 36, 52);

    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly BoardViewService _boardService = new();

    private bool _mineOnly, _sprintOnly, _bugsOnly, _unassignedOnly;
    private IReadOnlyList<WorkItem> _allItems = [];
    private IReadOnlyList<BoardColumnDefinition> _columns = [];
    private DevPulse.Core.Models.AppSettings _appSettings = new();
    private readonly Dictionary<string, BoardColumnPanel> _columnPanels = new();

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
    }

    public async Task LoadAsync()
    {
        _allItems = await _store.GetWorkItemsAsync();
        _columns = await _settings.GetBoardColumnsAsync();
        _appSettings = await _settings.GetAppSettingsAsync();

        var assignees = _allItems
            .Where(i => !string.IsNullOrEmpty(i.AssignedToDisplayName))
            .Select(i => i.AssignedToDisplayName)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        void ApplyUi()
        {
            _assigneeFilter.Items.Clear();
            _assigneeFilter.Items.Add("All assignees");
            foreach (var a in assignees) _assigneeFilter.Items.Add(a);
            if (_assigneeFilter.SelectedIndex < 0) _assigneeFilter.SelectedIndex = 0;
            ApplyFilters();
        }

        if (InvokeRequired) Invoke(ApplyUi);
        else ApplyUi();
    }

    private void ApplyFilters()
    {
        if (InvokeRequired) { Invoke(ApplyFilters); return; }

        var settings = _appSettings;
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

        var grouped = _boardService.GroupByColumn(filteredItems, _columns);
        var orderedColumns = _columns.OrderBy(c => c.Order).ToList();
        var activeNames = orderedColumns.Select(c => c.Name).ToHashSet();

        foreach (var gone in _columnPanels.Keys.Except(activeNames).ToList())
        {
            _boardPanel.Controls.Remove(_columnPanels[gone]);
            _columnPanels[gone].Dispose();
            _columnPanels.Remove(gone);
        }

        int x = 8;
        int colIndex = 0;

        foreach (var col in orderedColumns)
        {
            grouped.TryGetValue(col.Name, out var colItems);
            colItems ??= [];

            if (!_columnPanels.TryGetValue(col.Name, out var panel))
            {
                panel = new BoardColumnPanel(col.Name, colIndex)
                {
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                };
                _columnPanels[col.Name] = panel;
                _boardPanel.Controls.Add(panel);
            }

            panel.Height = _boardPanel.Height - 20;
            panel.Left = x;
            panel.Top = 4;
            panel.SetItems(colItems);

            x += panel.Width + 8;
            colIndex++;
        }

        _boardPanel.ResumeLayout(true);
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e) => ApplyFilters();
    private void TypeFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilters();
    private void AssigneeFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilters();
    private void PriorityFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilters();

    private void BtnMineOnly_Click(object? sender, EventArgs e)
    {
        _mineOnly = !_mineOnly;
        _btnMineOnly.BackColor = _mineOnly ? System.Drawing.Color.FromArgb(80, 80, 140) : System.Drawing.Color.FromArgb(50, 50, 78);
        ApplyFilters();
    }

    private void BtnSprintOnly_Click(object? sender, EventArgs e)
    {
        _sprintOnly = !_sprintOnly;
        _btnSprintOnly.BackColor = _sprintOnly ? System.Drawing.Color.FromArgb(80, 80, 140) : System.Drawing.Color.FromArgb(50, 50, 78);
        ApplyFilters();
    }

    private void BtnBugsOnly_Click(object? sender, EventArgs e)
    {
        _bugsOnly = !_bugsOnly;
        _btnBugsOnly.BackColor = _bugsOnly ? System.Drawing.Color.FromArgb(80, 80, 140) : System.Drawing.Color.FromArgb(50, 50, 78);
        ApplyFilters();
    }

    private void BtnUnassignedOnly_Click(object? sender, EventArgs e)
    {
        _unassignedOnly = !_unassignedOnly;
        _btnUnassignedOnly.BackColor = _unassignedOnly ? System.Drawing.Color.FromArgb(80, 80, 140) : System.Drawing.Color.FromArgb(50, 50, 78);
        ApplyFilters();
    }

    private async void BtnRefresh_Click(object? sender, EventArgs e)
    {
        try { await LoadAsync(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "BoardForm: Refresh failed"); }
    }
}
