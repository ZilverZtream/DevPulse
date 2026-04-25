using DevPulse.App;
using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using DevPulse.Core.Services;

namespace DevPulse.App.Forms;

public readonly record struct BoardErrorState(bool RequiresUserAction, string? Reason, PollErrorKind Kind);

public sealed partial class BoardForm : Form
{
    private static readonly Color DarkBg = Color.FromArgb(30, 30, 46);
    private static readonly Color ToolbarBg = Color.FromArgb(36, 36, 52);

    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly WindowBoundsService _bounds;
    private readonly BoardViewService _boardService = new();

    private bool _mineOnly, _sprintOnly, _bugsOnly, _unassignedOnly;
    private IReadOnlyList<WorkItem> _allItems = [];
    private IReadOnlyList<BoardColumnDefinition> _columns = [];
    private DevPulse.Core.Models.AppSettings _appSettings = new();
    private readonly Dictionary<string, BoardColumnPanel> _columnPanels = new();
    private int _loading;
    private Label? _emptyStateLabel;

    private AiPipelineService? _aiPipeline;
    private IReadOnlyList<IAiProvider> _aiProviders = [];
    private IReadOnlyList<AiTemplate> _aiTemplates = [];

    // Tray wires these post-construction so BoardForm doesn't need a direct reference to TrayApplicationContext.
    public Func<BoardErrorState>? ErrorStateProvider { get; set; }
    public Action? OpenSettingsAction { get; set; }

    private System.Windows.Forms.Timer? _errorPollTimer;

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
        _bounds = new WindowBoundsService(store);
        InitializeComponent();

        KeyPreview = true;
        KeyDown += BoardForm_KeyDown;

        Load += BoardForm_Load;
        FormClosing += BoardForm_FormClosing;

        // Periodic ping refreshes the auth banner from the polling services (E3) without coupling to TrayApplicationContext.
        // components is initialized in InitializeComponent() above — null-forgiving is safe here.
        _errorPollTimer = new System.Windows.Forms.Timer(components!) { Interval = 5000 };
        _errorPollTimer.Tick += (_, _) => RefreshErrorBanner();
        _errorPollTimer.Start();
    }

    private async void BoardForm_Load(object? sender, EventArgs e)
    {
        try
        {
            var record = await _bounds.LoadAsync(WindowBoundsService.BoardFormKey);
            if (IsDisposed) return;
            WindowBoundsService.ApplyOnLoad(this, record);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "BoardForm: bounds load failed"); }
    }

    private void BoardForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        var record = WindowBoundsService.CaptureBounds(this);
        if (record is null) return;
        // Fire-and-forget — closing the form shouldn't await disk I/O. Errors are logged inside SaveAsync.
        _ = _bounds.SaveAsync(WindowBoundsService.BoardFormKey, record);
    }

    /// <summary>Public hook so callers (e.g. TrayApplicationContext on PollCompleted) can push a fresh banner update.</summary>
    public void RefreshErrorBanner()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke(RefreshErrorBanner); } catch (ObjectDisposedException) { } return; }

        var state = ErrorStateProvider?.Invoke() ?? default;
        UpdateErrorBanner(state);
    }

    private void UpdateErrorBanner(BoardErrorState state)
    {
        if (!state.RequiresUserAction && state.Kind != PollErrorKind.Transient)
        {
            _errorBanner.Visible = false;
            return;
        }

        // Yellow for transient/throttled-but-stale data; red for auth/permanent failures.
        var isStale = state.Kind == PollErrorKind.Transient || state.Kind == PollErrorKind.Throttled;
        if (isStale && !state.RequiresUserAction)
        {
            _errorBanner.BackColor = System.Drawing.Color.FromArgb(58, 54, 31);
            _errorBannerLabel.ForeColor = System.Drawing.Color.FromArgb(250, 204, 21);
            _errorBannerSettingsBtn.BackColor = System.Drawing.Color.FromArgb(80, 70, 30);
            _errorBannerSettingsBtn.ForeColor = System.Drawing.Color.FromArgb(240, 230, 200);
            _errorBannerSettingsBtn.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(160, 130, 60);
        }
        else
        {
            _errorBanner.BackColor = System.Drawing.Color.FromArgb(58, 31, 31);
            _errorBannerLabel.ForeColor = System.Drawing.Color.FromArgb(255, 136, 136);
            _errorBannerSettingsBtn.BackColor = System.Drawing.Color.FromArgb(80, 40, 40);
            _errorBannerSettingsBtn.ForeColor = System.Drawing.Color.FromArgb(240, 220, 220);
            _errorBannerSettingsBtn.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(160, 80, 80);
        }

        _errorBannerLabel.Text = state.Kind switch
        {
            PollErrorKind.AuthRequired => "Authentication failed — open Settings to update your PAT.",
            PollErrorKind.Permanent => state.Reason ?? "Configuration error — open Settings to fix organization/project/area paths.",
            _ => state.Reason ?? "Board data may be stale — last refresh failed."
        };

        _errorBanner.Visible = true;
    }

    private void ErrorBannerSettings_Click(object? sender, EventArgs e)
    {
        OpenSettingsAction?.Invoke();
    }

    public void AttachAi(AiPipelineService pipeline, IEnumerable<IAiProvider> providers, IReadOnlyList<AiTemplate> templates)
    {
        _aiPipeline = pipeline;
        _aiProviders = providers.ToList();
        _aiTemplates = templates;
    }

    public async Task LoadAsync()
    {
        if (Interlocked.CompareExchange(ref _loading, 1, 0) != 0) return;
        try
        {
            await LoadCoreAsync();
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }
    }

    private async Task LoadCoreAsync()
    {
        _allItems = await _store.GetWorkItemsAsync();
        _columns = await _settings.GetBoardColumnsAsync();
        _appSettings = await _settings.GetAppSettingsAsync();
        _boardService.RecomputeAging(_allItems, _columns);

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

        // Show empty-state hint when there's nothing to render — distinguishes "nothing to show" from "app broken"
        var showEmpty = filteredItems.Count == 0;
        if (showEmpty)
        {
            if (_emptyStateLabel == null)
            {
                _emptyStateLabel = new Label
                {
                    Text = _allItems.Count == 0
                        ? "No work items found. Check your Area Path in Settings."
                        : "No work items match the current filters.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Color.FromArgb(180, 180, 200),
                    BackColor = DarkBg,
                    Font = new Font("Segoe UI", 11f, FontStyle.Regular)
                };
                _boardPanel.Controls.Add(_emptyStateLabel);
                _emptyStateLabel.BringToFront();
            }
            else
            {
                _emptyStateLabel.Text = _allItems.Count == 0
                    ? "No work items found. Check your Area Path in Settings."
                    : "No work items match the current filters.";
                _emptyStateLabel.Visible = true;
            }
        }
        else if (_emptyStateLabel != null)
        {
            _emptyStateLabel.Visible = false;
        }

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
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom,
                    CardBinder = BindAiHandlersToCard
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

    private void BindAiHandlersToCard(WorkItemCard card)
    {
        // F6 — single shared ToolTip on the form, set per-card after the card is built.
        _cardTooltip.SetToolTip(card, card.BuildTooltipText());

        card.OnDraftRequested += (_, _) =>
        {
            if (_aiPipeline == null) return;
            using var dlg = new AiGenerateDialog(_aiPipeline, _aiProviders, _aiTemplates, card.Item);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var review = new AiReviewForm(
                    (IAiAttemptStore)_store, _aiPipeline, _aiProviders, _aiTemplates, card.Item);
                review.Show(this);
            }
        };
        card.OnViewDraftsRequested += (_, _) =>
        {
            if (_aiPipeline == null) return;
            var review = new AiReviewForm(
                (IAiAttemptStore)_store, _aiPipeline, _aiProviders, _aiTemplates, card.Item);
            review.Show(this);
        };
        card.OnOpenFolderRequested += (_, _) =>
        {
            var root = _appSettings.AiOutputRootPath;
            var slug = Slugify.Project(_appSettings.Project);
            var folder = System.IO.Path.Combine(root, slug, card.Item.Id.ToString());
            if (System.IO.Directory.Exists(folder))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        };
    }

    // Debounced — fast typists shouldn't trigger ApplyFilters on every keystroke (F8).
    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _filterDebounceTimer.Stop();
        _filterDebounceTimer.Start();
    }

    private void FilterDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _filterDebounceTimer.Stop();
        ApplyFilters();
    }

    private void TypeFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilters();
    private void AssigneeFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilters();
    private void PriorityFilter_SelectedIndexChanged(object? sender, EventArgs e) => ApplyFilters();

    private void BoardForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // F5 → Refresh
        if (e.KeyCode == Keys.F5 && !e.Control && !e.Alt && !e.Shift)
        {
            e.Handled = true;
            BtnRefresh_Click(this, EventArgs.Empty);
            return;
        }

        // Ctrl+F → focus search box
        if (e.Control && e.KeyCode == Keys.F)
        {
            e.Handled = true;
            _searchBox.Focus();
            _searchBox.SelectAll();
            return;
        }

        // Esc → clear filters and re-render
        if (e.KeyCode == Keys.Escape && !e.Control && !e.Alt && !e.Shift)
        {
            e.Handled = true;
            _filterDebounceTimer.Stop();
            _searchBox.Text = string.Empty;
            if (_typeFilter.Items.Count > 0) _typeFilter.SelectedIndex = 0;
            if (_assigneeFilter.Items.Count > 0) _assigneeFilter.SelectedIndex = 0;
            if (_priorityFilter.Items.Count > 0) _priorityFilter.SelectedIndex = 0;
            _mineOnly = _sprintOnly = _bugsOnly = _unassignedOnly = false;
            _btnMineOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
            _btnSprintOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
            _btnBugsOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
            _btnUnassignedOnly.BackColor = System.Drawing.Color.FromArgb(50, 50, 78);
            ApplyFilters();
            return;
        }

        // Ctrl+, → open Settings
        if (e.Control && e.KeyCode == Keys.Oemcomma)
        {
            e.Handled = true;
            OpenSettingsAction?.Invoke();
            return;
        }

        // Enter → open the first visible (non-dimmed) card's URL.
        // Cards aren't focusable yet — defer per-card focus tracking until card selection lands.
        if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt && !e.Shift)
        {
            // Skip if Enter is being routed by a focused TextBox/ComboBox so user form input still works
            if (ActiveControl is TextBox or ComboBox) return;

            var card = FindFirstVisibleCard();
            if (card != null && !string.IsNullOrEmpty(card.Item.WorkItemUrl))
            {
                e.Handled = true;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(card.Item.WorkItemUrl) { UseShellExecute = true });
                }
                catch (Exception ex) { Serilog.Log.Warning(ex, "BoardForm: failed to open work item URL"); }
            }
        }
    }

    private WorkItemCard? FindFirstVisibleCard()
    {
        foreach (var col in _columnPanels.Values)
        {
            foreach (Control inner in col.Controls)
            {
                // BoardColumnPanel exposes a card container Panel; walk into it
                foreach (Control c in inner.Controls)
                    if (c is WorkItemCard card && !card.Dimmed) return card;
            }
        }
        return null;
    }

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
