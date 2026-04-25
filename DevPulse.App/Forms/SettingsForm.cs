using DevPulse.App.Services;
using DevPulse.Core.Models;
using DevPulse.Infrastructure.AzureDevOps;
using DevPulse.Infrastructure.Security;

namespace DevPulse.App.Forms;

public sealed partial class SettingsForm : Form
{
    private readonly SettingsService _settings;
    private AppSettings _appSettings = new();
    private List<DevPulse.Core.Models.AiTemplate> _aiTemplates = [];
    private int _selectedTemplateIdx = -1;
    private List<InboxDefinition> _inboxes = [];
    private bool _suppressInboxSelChanged;

    public SettingsForm(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
        WireListEditHandlers();
        BindTooltips();
        _chkAutoStart.Checked = AutoStartService.IsEnabled();
        // Show a loading placeholder while DPAPI unwrap runs off the UI thread.
        _patBox.Text = string.Empty;
        _patBox.Enabled = false;
        _ = LoadSettingsAsync().ContinueWith(
            t => Serilog.Log.Error(t.Exception?.GetBaseException(), "SettingsForm: LoadSettings failed"),
            System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
            System.Threading.Tasks.TaskScheduler.Default);
    }

    // ── Load / Save ────────────────────────────────────────────────────────────

    private async Task LoadSettingsAsync()
    {
        _appSettings = await _settings.GetAppSettingsAsync();
        if (IsDisposed) return;
        _orgUrl.Text = _appSettings.OrganizationUrl;
        _project.Text = _appSettings.Project;
        _repoFilter.Text = _appSettings.RepositoryFilter;
        _currentUser.Text = _appSettings.CurrentUserCanonicalKey;
        // DPAPI unwrap can briefly block; offload so the SettingsForm stays responsive while opening.
        var pat = await Task.Run(() => SecretStore.LoadPat() ?? string.Empty);
        if (IsDisposed) return;
        _patBox.Text = pat;
        _patBox.Enabled = true;
        _prInterval.Value = Math.Clamp(_appSettings.PrPollingIntervalMinutes, (int)_prInterval.Minimum, (int)_prInterval.Maximum);
        _wiInterval.Value = Math.Clamp(_appSettings.WorkItemPollingIntervalMinutes, (int)_wiInterval.Minimum, (int)_wiInterval.Maximum);
        _refreshOnStartup.Checked = _appSettings.RefreshOnStartup;
        _botPatterns.Text = string.Join(", ", _appSettings.BotIdentityPatterns);
        _poQaGroup.Text = string.Join(", ", _appSettings.PoQaGroupCanonicalKeys);
        _areaPath.Text = _appSettings.AreaPath;
        _iterationPath.Text = _appSettings.IterationPath;

        var inboxes = await _settings.GetInboxDefinitionsAsync();
        if (IsDisposed) return;
        _inboxes = [.. inboxes.OrderBy(x => x.Order)];
        RebindInboxListBox(selectIndex: _inboxes.Count > 0 ? 0 : -1);

        var aliases = await _settings.GetIdentityAliasesAsync();
        if (IsDisposed) return;
        _aliasGrid.Rows.Clear();
        foreach (var a in aliases)
            _aliasGrid.Rows.Add(a.CanonicalKey, string.Join(", ", a.Variants));
        UpdateAliasButtons();

        var columns = await _settings.GetBoardColumnsAsync();
        if (IsDisposed) return;
        _columnsGrid.Rows.Clear();
        foreach (var c in columns.OrderBy(x => x.Order))
            _columnsGrid.Rows.Add(c.Name, string.Join(", ", c.MappedStates), c.AgingDaysWarning, c.AgingDaysStale);
        UpdateColumnsButtons();

        _txtAiRoot.Text = _appSettings.AiOutputRootPath;

        var profiles = await _settings.GetAiProviderProfilesAsync();
        if (IsDisposed) return;
        var claude = profiles.FirstOrDefault(p => p.ProviderId == "claude-cli");
        _chkClaudeEnabled.Checked = claude?.Enabled ?? false;
        _txtClaudePath.Text = claude?.ExecutablePath ?? "";
        var openRouter = profiles.FirstOrDefault(p => p.ProviderId == "openrouter");
        _chkOpenRouterEnabled.Checked = openRouter?.Enabled ?? false;
        _txtOpenRouterModel.Text = openRouter?.DefaultModel ?? "anthropic/claude-3.5-sonnet";
        var keyResult = DevPulse.Infrastructure.Security.SecretStore.TryLoadSecret("openrouter");
        _txtOpenRouterKey.Text = keyResult.IsOk ? (keyResult.Value ?? "") : "";

        _aiTemplates = (await new DevPulse.App.Services.SettingsAiTemplateStore(_settings).GetTemplatesAsync()).ToList();
        if (IsDisposed) return;
        _lstAiTemplates.Items.Clear();
        foreach (var t in _aiTemplates) _lstAiTemplates.Items.Add(t.Name);
        if (_lstAiTemplates.Items.Count > 0) _lstAiTemplates.SelectedIndex = 0;
        UpdateAiTplButtons();
    }

    private async Task SaveSettingsAsync()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(_orgUrl.Text))
            errors.Add("Organization URL is required.");
        if (string.IsNullOrWhiteSpace(_project.Text))
            errors.Add("Project name is required.");
        if (string.IsNullOrWhiteSpace(_currentUser.Text))
            errors.Add("Your email (canonical key) is required.");
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "DevPulse — Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

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
        _appSettings.AiOutputRootPath = _txtAiRoot.Text.Trim();

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
                aliases.Add(new IdentityAlias { CanonicalKey = canon, Variants = [.. variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] });
        }
        await _settings.SaveIdentityAliasesAsync(aliases);

        var columns = new List<BoardColumnDefinition>();
        int order = 0;
        foreach (DataGridViewRow row in _columnsGrid.Rows)
        {
            if (row.IsNewRow) continue;
            var name = row.Cells["name"].Value?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(name)) continue;
            var states = (row.Cells["states"].Value?.ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _ = int.TryParse(row.Cells["warn"].Value?.ToString(), out int warn);
            _ = int.TryParse(row.Cells["stale"].Value?.ToString(), out int stale);
            columns.Add(new BoardColumnDefinition { Name = name, Order = order++, MappedStates = states, AgingDaysWarning = warn, AgingDaysStale = stale });
        }
        await _settings.SaveBoardColumnsAsync(columns);

        // Persist inbox order/membership. Order is recomputed from the current list sequence so reorder
        // operations stick. Existing rules are preserved by editing the cached InboxDefinition entries in place.
        for (int i = 0; i < _inboxes.Count; i++)
            _inboxes[i].Order = i;
        await _settings.SaveInboxDefinitionsAsync(_inboxes);

        if (_selectedTemplateIdx >= 0 && _selectedTemplateIdx < _aiTemplates.Count)
        {
            _aiTemplates[_selectedTemplateIdx].RequiredHeaders =
                [.. _txtTemplateHeaders.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            _aiTemplates[_selectedTemplateIdx].PromptBody = _txtTemplateBody.Text;
        }

        var aiProfiles = new List<DevPulse.Core.Models.AiProviderProfile>
        {
            new() { ProviderId = "claude-cli", Enabled = _chkClaudeEnabled.Checked,
                    ExecutablePath = _txtClaudePath.Text.Trim(), DefaultModel = "" },
            new() { ProviderId = "openrouter", Enabled = _chkOpenRouterEnabled.Checked,
                    ExecutablePath = "", DefaultModel = _txtOpenRouterModel.Text.Trim() }
        };

        await _settings.SaveAiConfigAsync(aiProfiles, _aiTemplates);

        // Save OpenRouter key via DPAPI AFTER the transactional KV write — if this fails, provider/template config is already safe.
        if (!string.IsNullOrWhiteSpace(_txtOpenRouterKey.Text))
            DevPulse.Infrastructure.Security.SecretStore.SaveSecret("openrouter", _txtOpenRouterKey.Text);

        if (_chkAutoStart.Checked)
            AutoStartService.Enable(Application.ExecutablePath);
        else
            AutoStartService.Disable();

        MessageBox.Show("Settings saved.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void LoadInboxRules()
    {
        var idx = _inboxList.SelectedIndex;
        if (idx < 0 || idx >= _inboxes.Count) { _inboxRulesJson.Text = string.Empty; return; }
        // Read from the in-memory cache so newly-added inboxes (not yet persisted) are visible.
        var json = System.Text.Json.JsonSerializer.Serialize(_inboxes[idx], new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        _inboxRulesJson.Text = json;
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            var orgUrl = _orgUrl.Text.TrimEnd('/');
            var pat = _patBox.Text;
            using var handler = new AzureDevOpsAuthHandler(orgUrl, pat);
            using var http = new System.Net.Http.HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            const string adoApiVersion = "7.1";
            var resp = await http.GetAsync($"{orgUrl}/_apis/projects?api-version={adoApiVersion}");
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
        MessageBox.Show($"Exported to {dlg.FileName}\nNote: Personal Access Token is stored in the Windows credential store and is not included in this export.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void InboxList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressInboxSelChanged) return;
        LoadInboxRules();
        UpdateInboxButtons();
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        try { await SaveSettingsAsync(); }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SettingsForm: Save failed");
            MessageBox.Show($"Save failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void BtnTest_Click(object? sender, EventArgs e)
    {
        try { await TestConnectionAsync(); }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SettingsForm: TestConnection failed");
            MessageBox.Show($"Connection test failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e) => ExportSettings();

    private void LstAiTemplates_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_selectedTemplateIdx >= 0 && _selectedTemplateIdx < _aiTemplates.Count)
        {
            _aiTemplates[_selectedTemplateIdx].RequiredHeaders =
                [.. _txtTemplateHeaders.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            _aiTemplates[_selectedTemplateIdx].PromptBody = _txtTemplateBody.Text;
        }
        _selectedTemplateIdx = _lstAiTemplates.SelectedIndex;
        UpdateAiTplButtons();
        if (_selectedTemplateIdx < 0 || _selectedTemplateIdx >= _aiTemplates.Count) return;
        var t = _aiTemplates[_selectedTemplateIdx];
        _txtTemplateHeaders.Text = string.Join(", ", t.RequiredHeaders);
        _txtTemplateBody.Text = t.PromptBody;
    }

    private void BtnClaudeDetect_Click(object? sender, EventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("where", "claude")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(3000);
            var first = p.StandardOutput.ReadToEnd().Split('\n').FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(first)) _txtClaudePath.Text = first;
            else MessageBox.Show("claude not found on PATH.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Detect failed: {ex.Message}", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── F4: Add / Remove / Reorder buttons for list-like editors ───────────────

    private void WireListEditHandlers()
    {
        _btnAliasAdd.Click += (_, _) => AliasAdd();
        _btnAliasRemove.Click += (_, _) => AliasRemove();
        _btnAliasUp.Click += (_, _) => AliasMove(-1);
        _btnAliasDown.Click += (_, _) => AliasMove(+1);
        _aliasGrid.SelectionChanged += (_, _) => UpdateAliasButtons();
        _aliasGrid.KeyDown += AliasGrid_KeyDown;

        _btnColumnsAdd.Click += (_, _) => ColumnsAdd();
        _btnColumnsRemove.Click += (_, _) => ColumnsRemove();
        _btnColumnsUp.Click += (_, _) => ColumnsMove(-1);
        _btnColumnsDown.Click += (_, _) => ColumnsMove(+1);
        _columnsGrid.SelectionChanged += (_, _) => UpdateColumnsButtons();
        _columnsGrid.KeyDown += ColumnsGrid_KeyDown;

        _btnInboxAdd.Click += (_, _) => InboxAdd();
        _btnInboxRemove.Click += (_, _) => InboxRemove();
        _btnInboxUp.Click += (_, _) => InboxMove(-1);
        _btnInboxDown.Click += (_, _) => InboxMove(+1);
        _inboxList.KeyDown += InboxList_KeyDown;

        _btnAiTplAdd.Click += (_, _) => AiTplAdd();
        _btnAiTplRemove.Click += (_, _) => AiTplRemove();
        _btnAiTplUp.Click += (_, _) => AiTplMove(-1);
        _btnAiTplDown.Click += (_, _) => AiTplMove(+1);
        _lstAiTemplates.KeyDown += AiTplList_KeyDown;

        UpdateAliasButtons();
        UpdateColumnsButtons();
        UpdateInboxButtons();
        UpdateAiTplButtons();
    }

    // Aliases ──────────────────────────────────────────────────────────────────
    private void AliasAdd()
    {
        int idx = _aliasGrid.Rows.Add("new.user@example.com", "");
        _aliasGrid.ClearSelection();
        _aliasGrid.Rows[idx].Selected = true;
        _aliasGrid.CurrentCell = _aliasGrid.Rows[idx].Cells[0];
        _aliasGrid.BeginEdit(true);
        UpdateAliasButtons();
    }

    private void AliasRemove()
    {
        if (_aliasGrid.CurrentRow == null || _aliasGrid.CurrentRow.IsNewRow) return;
        int idx = _aliasGrid.CurrentRow.Index;
        _aliasGrid.Rows.RemoveAt(idx);
        if (_aliasGrid.Rows.Count > 0)
        {
            int sel = Math.Min(idx, _aliasGrid.Rows.Count - 1);
            _aliasGrid.ClearSelection();
            _aliasGrid.Rows[sel].Selected = true;
            _aliasGrid.CurrentCell = _aliasGrid.Rows[sel].Cells[0];
        }
        UpdateAliasButtons();
    }

    private void AliasMove(int delta)
    {
        if (_aliasGrid.CurrentRow == null) return;
        int idx = _aliasGrid.CurrentRow.Index;
        int target = idx + delta;
        if (target < 0 || target >= _aliasGrid.Rows.Count) return;
        var row = _aliasGrid.Rows[idx];
        _aliasGrid.Rows.RemoveAt(idx);
        _aliasGrid.Rows.Insert(target, row);
        _aliasGrid.ClearSelection();
        _aliasGrid.Rows[target].Selected = true;
        _aliasGrid.CurrentCell = _aliasGrid.Rows[target].Cells[0];
        UpdateAliasButtons();
    }

    private void UpdateAliasButtons()
    {
        int idx = _aliasGrid.CurrentRow?.Index ?? -1;
        bool hasSel = idx >= 0;
        _btnAliasRemove.Enabled = hasSel;
        _btnAliasUp.Enabled = hasSel && idx > 0;
        _btnAliasDown.Enabled = hasSel && idx < _aliasGrid.Rows.Count - 1;
    }

    private void AliasGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Insert) { AliasAdd(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete && !_aliasGrid.IsCurrentCellInEditMode) { AliasRemove(); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Up) { AliasMove(-1); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Down) { AliasMove(+1); e.Handled = true; }
    }

    // Columns ──────────────────────────────────────────────────────────────────
    private void ColumnsAdd()
    {
        int idx = _columnsGrid.Rows.Add("New Column", "", 2, 6);
        _columnsGrid.ClearSelection();
        _columnsGrid.Rows[idx].Selected = true;
        _columnsGrid.CurrentCell = _columnsGrid.Rows[idx].Cells[0];
        _columnsGrid.BeginEdit(true);
        UpdateColumnsButtons();
    }

    private void ColumnsRemove()
    {
        if (_columnsGrid.CurrentRow == null || _columnsGrid.CurrentRow.IsNewRow) return;
        if (_columnsGrid.Rows.Count <= 1)
        {
            MessageBox.Show("Cannot remove the last board column.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int idx = _columnsGrid.CurrentRow.Index;
        _columnsGrid.Rows.RemoveAt(idx);
        if (_columnsGrid.Rows.Count > 0)
        {
            int sel = Math.Min(idx, _columnsGrid.Rows.Count - 1);
            _columnsGrid.ClearSelection();
            _columnsGrid.Rows[sel].Selected = true;
            _columnsGrid.CurrentCell = _columnsGrid.Rows[sel].Cells[0];
        }
        UpdateColumnsButtons();
    }

    private void ColumnsMove(int delta)
    {
        if (_columnsGrid.CurrentRow == null) return;
        int idx = _columnsGrid.CurrentRow.Index;
        int target = idx + delta;
        if (target < 0 || target >= _columnsGrid.Rows.Count) return;
        var row = _columnsGrid.Rows[idx];
        _columnsGrid.Rows.RemoveAt(idx);
        _columnsGrid.Rows.Insert(target, row);
        _columnsGrid.ClearSelection();
        _columnsGrid.Rows[target].Selected = true;
        _columnsGrid.CurrentCell = _columnsGrid.Rows[target].Cells[0];
        UpdateColumnsButtons();
    }

    private void UpdateColumnsButtons()
    {
        int idx = _columnsGrid.CurrentRow?.Index ?? -1;
        bool hasSel = idx >= 0;
        _btnColumnsRemove.Enabled = hasSel && _columnsGrid.Rows.Count > 1;
        _btnColumnsUp.Enabled = hasSel && idx > 0;
        _btnColumnsDown.Enabled = hasSel && idx < _columnsGrid.Rows.Count - 1;
    }

    private void ColumnsGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Insert) { ColumnsAdd(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete && !_columnsGrid.IsCurrentCellInEditMode) { ColumnsRemove(); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Up) { ColumnsMove(-1); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Down) { ColumnsMove(+1); e.Handled = true; }
    }

    // Inboxes ──────────────────────────────────────────────────────────────────
    private void RebindInboxListBox(int selectIndex)
    {
        _suppressInboxSelChanged = true;
        try
        {
            _inboxList.BeginUpdate();
            _inboxList.Items.Clear();
            foreach (var i in _inboxes) _inboxList.Items.Add(i.Name);
            _inboxList.EndUpdate();
            if (selectIndex >= 0 && selectIndex < _inboxList.Items.Count)
                _inboxList.SelectedIndex = selectIndex;
        }
        finally { _suppressInboxSelChanged = false; }
        LoadInboxRules();
        UpdateInboxButtons();
    }

    private void InboxAdd()
    {
        var name = PromptForName("Add inbox", "Inbox name:", "New Inbox");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_inboxes.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"An inbox named '{name}' already exists.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _inboxes.Add(new InboxDefinition
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            Order = _inboxes.Count,
            IsEnabled = true,
            ShowNotifications = true,
            Rules = []
        });
        RebindInboxListBox(_inboxes.Count - 1);
    }

    private void InboxRemove()
    {
        int idx = _inboxList.SelectedIndex;
        if (idx < 0 || idx >= _inboxes.Count) return;
        if (_inboxes.Count <= 1)
        {
            MessageBox.Show("Cannot remove the last inbox — DevPulse needs at least one inbox to route events.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var target = _inboxes[idx];
        if (target.IsSystemInbox)
        {
            MessageBox.Show($"'{target.Name}' is a system inbox and cannot be removed.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var confirm = MessageBox.Show($"Remove inbox '{target.Name}'?\nEvents already routed to it will remain in the database until cleanup.", "DevPulse", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;
        _inboxes.RemoveAt(idx);
        RebindInboxListBox(Math.Min(idx, _inboxes.Count - 1));
    }

    private void InboxMove(int delta)
    {
        int idx = _inboxList.SelectedIndex;
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= _inboxes.Count) return;
        (_inboxes[idx], _inboxes[target]) = (_inboxes[target], _inboxes[idx]);
        RebindInboxListBox(target);
    }

    private void UpdateInboxButtons()
    {
        int idx = _inboxList.SelectedIndex;
        bool hasSel = idx >= 0;
        _btnInboxRemove.Enabled = hasSel && _inboxes.Count > 1
            && idx < _inboxes.Count && !_inboxes[idx].IsSystemInbox;
        _btnInboxUp.Enabled = hasSel && idx > 0;
        _btnInboxDown.Enabled = hasSel && idx < _inboxes.Count - 1;
    }

    private void InboxList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Insert) { InboxAdd(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete) { InboxRemove(); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Up) { InboxMove(-1); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Down) { InboxMove(+1); e.Handled = true; }
    }

    // AI templates ─────────────────────────────────────────────────────────────
    private void AiTplAdd()
    {
        var name = PromptForName("Add AI template", "Template name:", "New Template");
        if (string.IsNullOrWhiteSpace(name)) return;
        // Flush pending edits to the currently-selected template before adding a new one.
        if (_selectedTemplateIdx >= 0 && _selectedTemplateIdx < _aiTemplates.Count)
        {
            _aiTemplates[_selectedTemplateIdx].RequiredHeaders =
                [.. _txtTemplateHeaders.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            _aiTemplates[_selectedTemplateIdx].PromptBody = _txtTemplateBody.Text;
        }
        _aiTemplates.Add(new DevPulse.Core.Models.AiTemplate
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            AppliesTo = [],
            RequiredHeaders = [],
            PromptBody = string.Empty
        });
        _lstAiTemplates.Items.Add(name.Trim());
        _lstAiTemplates.SelectedIndex = _aiTemplates.Count - 1;
        UpdateAiTplButtons();
    }

    private void AiTplRemove()
    {
        int idx = _lstAiTemplates.SelectedIndex;
        if (idx < 0 || idx >= _aiTemplates.Count) return;
        if (_aiTemplates.Count <= 1)
        {
            MessageBox.Show("Cannot remove the last AI template.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var confirm = MessageBox.Show($"Remove template '{_aiTemplates[idx].Name}'?", "DevPulse", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;
        // Clear cached selection so the SelectedIndexChanged handler doesn't try to flush
        // edits back into a now-removed template.
        _selectedTemplateIdx = -1;
        _aiTemplates.RemoveAt(idx);
        _lstAiTemplates.Items.RemoveAt(idx);
        if (_aiTemplates.Count > 0)
            _lstAiTemplates.SelectedIndex = Math.Min(idx, _aiTemplates.Count - 1);
        else
        {
            _txtTemplateHeaders.Text = string.Empty;
            _txtTemplateBody.Text = string.Empty;
        }
        UpdateAiTplButtons();
    }

    private void AiTplMove(int delta)
    {
        int idx = _lstAiTemplates.SelectedIndex;
        int target = idx + delta;
        if (idx < 0 || target < 0 || target >= _aiTemplates.Count) return;
        // Flush pending body/headers edits before reordering, otherwise the swap loses unsaved changes.
        if (_selectedTemplateIdx >= 0 && _selectedTemplateIdx < _aiTemplates.Count)
        {
            _aiTemplates[_selectedTemplateIdx].RequiredHeaders =
                [.. _txtTemplateHeaders.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            _aiTemplates[_selectedTemplateIdx].PromptBody = _txtTemplateBody.Text;
        }
        (_aiTemplates[idx], _aiTemplates[target]) = (_aiTemplates[target], _aiTemplates[idx]);
        _selectedTemplateIdx = -1;
        _lstAiTemplates.Items.Clear();
        foreach (var t in _aiTemplates) _lstAiTemplates.Items.Add(t.Name);
        _lstAiTemplates.SelectedIndex = target;
        UpdateAiTplButtons();
    }

    private void UpdateAiTplButtons()
    {
        int idx = _lstAiTemplates.SelectedIndex;
        bool hasSel = idx >= 0;
        _btnAiTplRemove.Enabled = hasSel && _aiTemplates.Count > 1;
        _btnAiTplUp.Enabled = hasSel && idx > 0;
        _btnAiTplDown.Enabled = hasSel && idx < _aiTemplates.Count - 1;
    }

    private void AiTplList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Insert) { AiTplAdd(); e.Handled = true; }
        else if (e.KeyCode == Keys.Delete) { AiTplRemove(); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Up) { AiTplMove(-1); e.Handled = true; }
        else if (e.Alt && e.KeyCode == Keys.Down) { AiTplMove(+1); e.Handled = true; }
    }

    // Tiny dark-themed name prompt — avoids dragging in a dependency for one input dialog.
    private static string? PromptForName(string title, string label, string defaultValue)
    {
        using var dlg = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new System.Drawing.Size(360, 120),
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = System.Drawing.Color.FromArgb(30, 30, 46),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235)
        };
        var lbl = new Label { Text = label, AutoSize = true, Left = 12, Top = 14, ForeColor = System.Drawing.Color.FromArgb(180, 180, 200) };
        var txt = new TextBox
        {
            Left = 12,
            Top = 38,
            Width = 336,
            Text = defaultValue,
            BackColor = System.Drawing.Color.FromArgb(42, 42, 62),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235),
            BorderStyle = BorderStyle.FixedSingle
        };
        var ok = new Button
        {
            Text = "OK",
            Left = 192,
            Top = 76,
            Width = 72,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = System.Drawing.Color.FromArgb(60, 100, 160),
            ForeColor = System.Drawing.Color.White
        };
        var cancel = new Button
        {
            Text = "Cancel",
            Left = 272,
            Top = 76,
            Width = 76,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = System.Drawing.Color.FromArgb(42, 42, 62),
            ForeColor = System.Drawing.Color.FromArgb(220, 220, 235)
        };
        ok.FlatAppearance.BorderSize = 0;
        cancel.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(58, 58, 82);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Controls.AddRange([lbl, txt, ok, cancel]);
        txt.SelectAll();
        return dlg.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }

    // ── Tooltips ───────────────────────────────────────────────────────────────
    // Applies tooltips to every meaningful input on every tab. Centralised so adding a new
    // setting in the Designer also gets a hint here without scattering SetToolTip calls.
    private void BindTooltips()
    {
        var t = _toolTips;

        // Connection
        t.SetToolTip(_orgUrl, "Your Azure DevOps organization URL, e.g. https://dev.azure.com/contoso");
        t.SetToolTip(_project, "Default Azure DevOps project to poll");
        t.SetToolTip(_repoFilter, "Optional: limit PR polling to one repository (leave blank for all repos in the project)");
        t.SetToolTip(_currentUser, "Your canonical email — used to detect PRs assigned to you and your votes");
        t.SetToolTip(_patBox, "Personal Access Token (Code: Read, Work Items: Read). Stored encrypted via Windows DPAPI");
        t.SetToolTip(_btnTest, "Send a single request to Azure DevOps using the values above to verify connectivity");

        // Polling
        t.SetToolTip(_prInterval, "How often to check for new pull request activity (minutes)");
        t.SetToolTip(_wiInterval, "How often to refresh the Kanban board (minutes)");
        t.SetToolTip(_refreshOnStartup, "Run a polling cycle immediately when DevPulse starts, instead of waiting for the first interval");

        // Identities
        t.SetToolTip(_botPatterns, "Regular expressions matching bot accounts. Bot comments are collapsed in inbox.");
        t.SetToolTip(_poQaGroup, "Canonical keys (emails) for PO/QA reviewers — used by routing rules that target this group");
        t.SetToolTip(_aliasGrid, "Map alternate names/emails to a single canonical identity so votes and comments deduplicate correctly");
        t.SetToolTip(_btnAliasAdd, "Add a new alias row (Insert)");
        t.SetToolTip(_btnAliasRemove, "Remove the selected alias (Delete)");
        t.SetToolTip(_btnAliasUp, "Move the selected alias up (Alt+Up)");
        t.SetToolTip(_btnAliasDown, "Move the selected alias down (Alt+Down)");

        // Inboxes
        t.SetToolTip(_inboxList, "Inboxes are evaluated top-to-bottom. The first matching rule wins.");
        t.SetToolTip(_inboxRulesJson, "JSON view of the selected inbox definition (read-only preview)");
        t.SetToolTip(_btnInboxAdd, "Add a new inbox (Insert)");
        t.SetToolTip(_btnInboxRemove, "Remove the selected inbox (Delete) — system inboxes cannot be removed");
        t.SetToolTip(_btnInboxUp, "Move the selected inbox up — affects rule evaluation order (Alt+Up)");
        t.SetToolTip(_btnInboxDown, "Move the selected inbox down — affects rule evaluation order (Alt+Down)");

        // Board
        t.SetToolTip(_areaPath, "Azure DevOps area path used for the work item WIQL query (e.g. Project\\Team)");
        t.SetToolTip(_iterationPath, "Optional iteration path filter — leave blank to include all iterations");
        t.SetToolTip(_columnsGrid, "Define Kanban board columns and which ADO states map to each. Aging days control card highlighting.");
        t.SetToolTip(_btnColumnsAdd, "Add a new column (Insert)");
        t.SetToolTip(_btnColumnsRemove, "Remove the selected column (Delete)");
        t.SetToolTip(_btnColumnsUp, "Move the selected column left (Alt+Up)");
        t.SetToolTip(_btnColumnsDown, "Move the selected column right (Alt+Down)");

        // Advanced
        t.SetToolTip(_btnExport, "Export non-secret settings to a JSON file. Personal Access Token is excluded.");
        t.SetToolTip(_chkAutoStart, "Add DevPulse to your per-user Windows Run key so it launches when you sign in");
        t.SetToolTip(_btnSave, "Save all changes across every tab");

        // AI
        t.SetToolTip(_txtAiRoot, "Root folder where AI review outputs are written (one subfolder per work item)");
        t.SetToolTip(_chkClaudeEnabled, "Enable the Claude Code CLI provider for AI reviews");
        t.SetToolTip(_txtClaudePath, "Full path to the claude executable (e.g. C:\\Users\\you\\AppData\\Roaming\\npm\\claude.cmd)");
        t.SetToolTip(_btnClaudeDetect, "Run 'where claude' to locate the CLI on your PATH");
        t.SetToolTip(_chkOpenRouterEnabled, "Enable the OpenRouter HTTP provider for AI reviews");
        t.SetToolTip(_txtOpenRouterKey, "OpenRouter API key. Stored encrypted via Windows DPAPI.");
        t.SetToolTip(_txtOpenRouterModel, "Default OpenRouter model id, e.g. anthropic/claude-3.5-sonnet");
        t.SetToolTip(_lstAiTemplates, "AI review templates. Select a template to edit its required headers and prompt body.");
        t.SetToolTip(_txtTemplateHeaders, "Comma-separated markdown headers the AI output must contain (validated post-run)");
        t.SetToolTip(_txtTemplateBody, "Prompt body for this template. Tokens like {{Title}}, {{Description}} are substituted at run time.");
        t.SetToolTip(_btnAiTplAdd, "Add a new AI template (Insert)");
        t.SetToolTip(_btnAiTplRemove, "Remove the selected template (Delete)");
        t.SetToolTip(_btnAiTplUp, "Move the selected template up (Alt+Up)");
        t.SetToolTip(_btnAiTplDown, "Move the selected template down (Alt+Down)");
    }
}
