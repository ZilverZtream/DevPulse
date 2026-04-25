using System.Diagnostics;
using System.Text;
using DevPulse.Core.Enums;
using DevPulse.Core.Interfaces;
using DevPulse.Core.Models;

namespace DevPulse.Infrastructure.Ai;

public sealed class ClaudeCliProvider : IAiProvider
{
    private readonly string _executablePath;

    public ClaudeCliProvider(string executablePath)
    {
        _executablePath = executablePath ?? string.Empty;
    }

    public string Id => "claude-cli";
    public string DisplayName => "Claude Code CLI";
    public AiProviderKind Kind => AiProviderKind.Cli;
    public AiDataPolicy DataPolicy => AiDataPolicy.Local;

    public Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
            return Task.FromResult(new AiHealthResult(false, $"Executable not found: {_executablePath}"));
        return Task.FromResult(new AiHealthResult(true, null));
    }

    public async Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct = default)
    {
        if (!File.Exists(_executablePath))
            throw new InvalidOperationException($"Claude CLI not found at {_executablePath}");

        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = "--print",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try { await proc.StandardInput.WriteAsync(req.Prompt).ConfigureAwait(false); }
        finally { proc.StandardInput.Close(); }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(req.Timeout);

        try
        {
            await proc.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested)
                throw; // user / caller cancelled — surface OperationCanceledException
            throw new TimeoutException($"Claude CLI exceeded timeout {req.Timeout.TotalSeconds}s");
        }

        var exitCode = proc.ExitCode;
        var outText = stdout.ToString();
        var errText = stderr.ToString();

        if (exitCode != 0)
        {
            var tail = errText.Length > 500 ? errText[^500..] : errText;
            throw new InvalidOperationException($"Claude CLI exit code {exitCode}: {tail}");
        }

        if (string.IsNullOrWhiteSpace(outText))
            throw new InvalidOperationException("Claude CLI returned no output");

        return new AiGenerateResult(
            Markdown: outText,
            ModelUsed: "",
            TokensIn: 0,
            TokensOut: 0,
            Duration: sw.Elapsed,
            ErrorMessage: null);
    }
}
