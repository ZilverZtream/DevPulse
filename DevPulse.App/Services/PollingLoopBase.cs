using Serilog;

namespace DevPulse.App.Services;

public abstract class PollingLoopBase : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private Task? _loopTask;

    public event EventHandler? PollCompleted;
    public bool LastPollFailed { get; private set; }

    protected abstract string TrackName { get; }
    protected abstract Task ExecutePollAsync(CancellationToken ct);
    protected virtual Task OnPollFailedAsync(Exception ex, CancellationToken ct) => Task.CompletedTask;

    public void Start(int intervalMinutes)
    {
        var clamped = Math.Clamp(intervalMinutes, 1, 1440);
        _loopTask = RunLoopAsync(TimeSpan.FromMinutes(clamped), _cts.Token);
    }

    public Task RefreshNowAsync() => ExecuteSafeAsync(_cts.Token);

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        await ExecuteSafeAsync(ct);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await ExecuteSafeAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ExecuteSafeAsync(CancellationToken ct)
    {
        if (!await _runLock.WaitAsync(0, ct)) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            await ExecutePollAsync(ct);
            LastPollFailed = false;
            PollCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "{Track} poll cycle failed", TrackName);
            LastPollFailed = true;
            await OnPollFailedAsync(ex, ct);
        }
        finally
        {
            _runLock.Release();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _runLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
