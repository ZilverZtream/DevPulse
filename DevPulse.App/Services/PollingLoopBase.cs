using DevPulse.Core.Services;
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
    private volatile bool _lastPollFailed;
    public bool LastPollFailed => _lastPollFailed;

    // Set by ExecuteSafeAsync after classification — Wave E3 will render an auth/config banner from this.
    private volatile bool _lastErrorRequiresUserAction;
    public bool LastErrorRequiresUserAction => _lastErrorRequiresUserAction;

    private volatile string? _lastErrorReason;
    public string? LastErrorReason => _lastErrorReason;

    private PollErrorKind _lastErrorKind = PollErrorKind.Unknown;
    public PollErrorKind LastErrorKind => _lastErrorKind;

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
        try { return ExecuteSafeAsync(_cts.Token); }
        catch (ObjectDisposedException) { return Task.CompletedTask; }
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
            _lastPollFailed = false;
            _lastErrorRequiresUserAction = false;
            _lastErrorReason = null;
            _lastErrorKind = PollErrorKind.Unknown;
            // Guard handler — if a subscriber throws, it's a UI bug, not a poll failure
            try { PollCompleted?.Invoke(this, EventArgs.Empty); }
            catch (Exception handlerEx) { Log.Warning(handlerEx, "{Track} PollCompleted handler threw", TrackName); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var kind = PollErrorClassifier.Classify(ex);
            _lastErrorKind = kind;
            switch (kind)
            {
                case PollErrorKind.AuthRequired:
                    Log.Error(ex, "{Track} poll failed (AuthRequired) — user must re-enter PAT or fix permissions", TrackName);
                    _lastErrorRequiresUserAction = true;
                    _lastErrorReason = "Authentication required — check PAT and permissions.";
                    break;
                case PollErrorKind.Permanent:
                    Log.Error(ex, "{Track} poll failed (Permanent) — bad config or resource missing", TrackName);
                    _lastErrorRequiresUserAction = true;
                    _lastErrorReason = "Permanent error — check organization/project/area configuration.";
                    break;
                case PollErrorKind.Throttled:
                    Log.Warning(ex, "{Track} poll throttled (429) — will retry next cycle", TrackName);
                    _lastErrorRequiresUserAction = false;
                    _lastErrorReason = null;
                    break;
                default:
                    Log.Error(ex, "{Track} poll cycle failed", TrackName);
                    _lastErrorRequiresUserAction = false;
                    _lastErrorReason = null;
                    break;
            }
            _lastPollFailed = true;
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
        // Sync backstop only — TrayApplicationContext orchestrates async cleanup via
        // Application.ApplicationExit. Cancel here so the loop tears down on its own;
        // never wait on a UI-thread sync context (deadlock risk). For tests / rare
        // sync-only callers, offload the await to the thread-pool with a short bound.
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        if (_loopTask != null)
        {
            try { Task.Run(() => DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { }
            catch (ObjectDisposedException) { }
        }
        else
        {
            // No loop ever started — safe to dispose primitives synchronously.
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                try { _cts.Dispose(); } catch (ObjectDisposedException) { }
                try { _runLock.Dispose(); } catch (ObjectDisposedException) { }
            }
        }
        GC.SuppressFinalize(this);
    }
}
