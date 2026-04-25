using DevPulse.App.Services;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class AiGenerateDialog : Form
{
    private readonly AiPipelineService _pipeline;
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly IReadOnlyList<AiTemplate> _templates;
    private readonly WorkItem _workItem;
    private readonly CancellationTokenSource _cts = new();

    public AiAttempt? Result { get; private set; }

    public AiGenerateDialog(
        AiPipelineService pipeline,
        IReadOnlyList<IAiProvider> providers,
        IReadOnlyList<AiTemplate> templates,
        WorkItem workItem)
    {
        _pipeline = pipeline;
        _providers = providers;
        _templates = templates;
        _workItem = workItem;
        InitializeComponent();
        _lblHeader.Text = $"Work item #{workItem.Id} — {workItem.Title}";
        PopulateTemplates();
        PopulateProviders();
        FormClosing += (_, _) =>
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { /* already disposed */ }
        };
        Disposed += (_, _) => _cts.Dispose();
    }

    private void PopulateTemplates()
    {
        _cboTemplate.Items.Clear();
        foreach (var t in _templates)
            _cboTemplate.Items.Add(new TemplateRow(t));
        var defaultIdx = _templates.ToList().FindIndex(t =>
            t.AppliesTo.Any(a => a.Equals(_workItem.Type.ToString(), StringComparison.OrdinalIgnoreCase)));
        if (defaultIdx >= 0) _cboTemplate.SelectedIndex = defaultIdx;
        else if (_cboTemplate.Items.Count > 0) _cboTemplate.SelectedIndex = 0;
    }

    private void PopulateProviders()
    {
        _cboProvider.Items.Clear();
        foreach (var p in _providers)
            _cboProvider.Items.Add(new ProviderRow(p));
        if (_cboProvider.Items.Count > 0) _cboProvider.SelectedIndex = 0;
    }

    private void CboProvider_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_cboProvider.SelectedItem is not ProviderRow row) return;
        if (row.Provider.DataPolicy == AiDataPolicy.Cloud)
        {
            _lblWarning.Text = $"This will send the work item title, description, and your prompt to {row.Provider.DisplayName}. Review your org's data policy.";
            _lblWarning.Visible = true;
        }
        else _lblWarning.Visible = false;
    }

    private async void BtnGenerate_Click(object? sender, EventArgs e)
    {
        if (_cboTemplate.SelectedItem is not TemplateRow tpl) return;
        if (_cboProvider.SelectedItem is not ProviderRow prov) return;

        _btnGenerate.Enabled = false;
        _btnGenerate.Text = "Generating…";
        _cboTemplate.Enabled = false;
        _cboProvider.Enabled = false;

        try
        {
            var model = _txtModel.Text?.Trim() ?? string.Empty;
            Result = await _pipeline.GenerateAsync(_workItem.Id, tpl.Template.Id, prov.Provider.Id, model, _cts.Token);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI generate dialog failed");
            MessageBox.Show($"Generation failed: {ex.Message}", "DevPulse",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnGenerate.Enabled = true;
            _btnGenerate.Text = "Generate";
            _cboTemplate.Enabled = true;
            _cboProvider.Enabled = true;
        }
    }

    private sealed record TemplateRow(AiTemplate Template)
    {
        public override string ToString() => Template.Name;
    }

    private sealed record ProviderRow(IAiProvider Provider)
    {
        public override string ToString()
        {
            var tag = Provider.DataPolicy == AiDataPolicy.Cloud ? "[CLOUD]" : "[LOCAL]";
            return $"{tag} {Provider.DisplayName}";
        }
    }
}
