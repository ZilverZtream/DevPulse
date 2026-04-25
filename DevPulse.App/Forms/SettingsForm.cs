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

    public SettingsForm(SettingsService settings)
    {
        _settings = settings;
        InitializeComponent();
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
        _inboxList.Items.Clear();
        foreach (var i in inboxes.OrderBy(x => x.Order))
            _inboxList.Items.Add(i.Name);

        var aliases = await _settings.GetIdentityAliasesAsync();
        if (IsDisposed) return;
        _aliasGrid.Rows.Clear();
        foreach (var a in aliases)
            _aliasGrid.Rows.Add(a.CanonicalKey, string.Join(", ", a.Variants));

        var columns = await _settings.GetBoardColumnsAsync();
        if (IsDisposed) return;
        _columnsGrid.Rows.Clear();
        foreach (var c in columns.OrderBy(x => x.Order))
            _columnsGrid.Rows.Add(c.Name, string.Join(", ", c.MappedStates), c.AgingDaysWarning, c.AgingDaysStale);

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

        MessageBox.Show("Settings saved.", "DevPulse", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void LoadInboxRules()
    {
        var selectedName = _inboxList.SelectedItem?.ToString();
        if (selectedName == null) return;
        _ = LoadInboxRulesAsync(selectedName).ContinueWith(
            t => Serilog.Log.Error(t.Exception?.GetBaseException(), "SettingsForm: LoadInboxRules failed"),
            System.Threading.CancellationToken.None,
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
            System.Threading.Tasks.TaskScheduler.Default);
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

    private void InboxList_SelectedIndexChanged(object? sender, EventArgs e) => LoadInboxRules();

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
}
