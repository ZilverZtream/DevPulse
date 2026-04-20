using Serilog;

namespace DevPulse.App.Services;

public abstract class PollingLoopBase : IDisposable, IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private Task? _loopTask;
    private int _started; // one-shot: Start() is idempotent after first call; does not reset on Dispose
    private int _disposed;

    public event EventHandler? PollCompleted;
    public bool LastPollFailed { get; private set; }

    protected abstract string TrackName { get; }
    protected abstract Task ExecutePollAsync(CancellationToken ct);
    protected virtual Task OnPollFailedAsync(Exception ex, CancellationToken ct) => Task.CompletedTask;

    public void Start(int intervalMinutes)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        var clamped = Math.Clamp(intervalMinutes, 1, 1440);
        _loopTask = RunLoopAsync(TimeSpan.FromMinutes(clamped), _cts.Token);
    }

    public Task RefreshNowAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return Task.CompletedTask;
        return ExecuteSafeAsync(_cts.Token);
    }

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        await ExecuteSafeAsync(ct).ConfigureAwait(false);
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await ExecuteSafeAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ExecuteSafeAsync(CancellationToken ct)
    {
        if (!await _runLock.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            ct.ThrowIfCancellationRequested();
            await ExecutePollAsync(ct).ConfigureAwait(false);
            LastPollFailed = false;
            PollCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Error(ex, "{Track} poll cycle failed", TrackName);
            LastPollFailed = true;
            await OnPollFailedAsync(ex, ct).ConfigureAwait(false);
        }
        finally
        {
            try { _runLock.Release(); }
            catch (ObjectDisposedException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _cts.Cancel();
        if (_loopTask != null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        _runLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(10));
    }
}
