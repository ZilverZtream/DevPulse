using DevPulse.App.Services;
using DevPulse.App.UI;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;
using Serilog;

namespace DevPulse.App.Forms;

public sealed partial class AiReviewForm : Form
{
    private readonly IAiAttemptStore _attempts;
    private readonly AiPipelineService _pipeline;
    private readonly IReadOnlyList<IAiProvider> _providers;
    private readonly IReadOnlyList<AiTemplate> _templates;
    private readonly WorkItem _workItem;

    private IReadOnlyList<AiAttempt> _history = [];
    private AiAttempt? _current;

    public AiReviewForm(
        IAiAttemptStore attempts,
        AiPipelineService pipeline,
        IReadOnlyList<IAiProvider> providers,
        IReadOnlyList<AiTemplate> templates,
        WorkItem workItem)
    {
        _attempts = attempts;
        _pipeline = pipeline;
        _providers = providers;
        _templates = templates;
        _workItem = workItem;
        InitializeComponent();
        Text = $"DevPulse — AI spec review — #{workItem.Id}";
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _history = await _attempts.GetAttemptsForWorkItemAsync(_workItem.Id);
            _lstHistory.Items.Clear();
            foreach (var a in _history)
                _lstHistory.Items.Add($"{a.CreatedAtUtc.ToLocalTime():MM/dd HH:mm}  [{a.Status}]  {a.ProviderId}");
            if (_lstHistory.Items.Count > 0)
                _lstHistory.SelectedIndex = 0;
        }
        catch (Exception ex) { Log.Error(ex, "AiReviewForm load failed"); }
    }

    private void LstHistory_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_lstHistory.SelectedIndex < 0 || _lstHistory.SelectedIndex >= _history.Count) return;
        _current = _history[_lstHistory.SelectedIndex];
        RenderCurrent();
    }

    private void RenderCurrent()
    {
        if (_current == null) return;

        (_lblStatus.Text, _lblStatus.BackColor) = _current.Status switch
        {
            AiAttemptStatus.Success => ($"✓ Generated via {_current.ProviderId} — {_current.DurationMs} ms — tokens in/out {_current.TokensIn}/{_current.TokensOut}",
                System.Drawing.Color.FromArgb(40, 100, 60)),
            AiAttemptStatus.ValidationFailed => ($"⚠ Validation failed — missing: {string.Join(", ", _current.MissingSections)}",
                System.Drawing.Color.FromArgb(140, 100, 40)),
            AiAttemptStatus.ProviderError => ($"✗ Provider error: {_current.ErrorMessage}",
                System.Drawing.Color.FromArgb(140, 60, 60)),
            AiAttemptStatus.Timeout => ($"✗ Timeout: {_current.ErrorMessage}",
                System.Drawing.Color.FromArgb(140, 60, 60)),
            _ => (_current.Status.ToString(), System.Drawing.Color.Gray)
        };

        try
        {
            var markdown = File.Exists(_current.SpecFilePath)
                ? File.ReadAllText(_current.SpecFilePath)
                : "(spec file missing)";
            _rtfSpec.Rtf = MarkdownRenderer.ToRtf(markdown);
        }
        catch (Exception ex)
        {
            _rtfSpec.Text = $"Failed to load spec: {ex.Message}";
        }

        _lblMetadata.Text =
            $"Template: {_current.TemplateId}\n" +
            $"Provider: {_current.ProviderId}\n" +
            $"Model: {_current.Model}\n" +
            $"Tokens in: {_current.TokensIn}\n" +
            $"Tokens out: {_current.TokensOut}\n" +
            $"Duration: {_current.DurationMs} ms\n" +
            $"Spec: {Path.GetFileName(_current.SpecFilePath)}\n" +
            $"Prompt: {Path.GetFileName(_current.PromptFilePath)}";
    }

    private async void BtnRegenerate_Click(object? sender, EventArgs e)
    {
        using var dlg = new AiGenerateDialog(_pipeline, _providers, _templates, _workItem);
        if (dlg.ShowDialog(this) == DialogResult.OK) await LoadAsync();
    }

    private void BtnCopy_Click(object? sender, EventArgs e)
    {
        if (_current == null || !File.Exists(_current.SpecFilePath)) return;
        Clipboard.SetText(File.ReadAllText(_current.SpecFilePath));
    }

    private void BtnOpenFolder_Click(object? sender, EventArgs e)
    {
        if (_current == null) return;
        var folder = Path.GetDirectoryName(_current.SpecFilePath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
