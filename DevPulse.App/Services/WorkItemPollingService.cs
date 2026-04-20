using DevPulse.Core.Interfaces;
using DevPulse.Core.Services;
using Serilog;

namespace DevPulse.App.Services;

public sealed class WorkItemPollingService : IDisposable
{
    private readonly IWorkItemClient _client;
    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly WorkItemNormalizer _normalizer = new();
    private readonly DebugLogService _debugLog;

    private System.Threading.Timer? _timer;
    private int _running;

    public event EventHandler? PollCompleted;
    public bool LastPollFailed { get; private set; }

    public WorkItemPollingService(
        IWorkItemClient client,
        IStateStore store,
        SettingsService settings,
        DebugLogService debugLog)
    {
        _client = client;
        _store = store;
        _settings = settings;
        _debugLog = debugLog;
    }

    public void Start(int intervalMinutes)
    {
        var ms = intervalMinutes * 60 * 1000;
        _timer = new System.Threading.Timer(async _ => await RunCycleAsync(), null, 0, ms);
    }

    public async Task RefreshNowAsync() => await RunCycleAsync();

    private async Task RunCycleAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        try
        {
            await ExecutePollAsync();
            LastPollFailed = false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Work item poll cycle failed");
            LastPollFailed = true;
            _debugLog.UpdatePollStatus("workitems", await _store.GetLastSuccessfulPollAsync("workitems"), null, 0, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task ExecutePollAsync()
    {
        var appSettings = await _settings.GetAppSettingsAsync();
        var columns = await _settings.GetBoardColumnsAsync();
        var now = DateTimeOffset.UtcNow;

        var dtos = await _client.GetWorkItemsAsync(appSettings.AreaPath, appSettings.IterationPath);
        var items = dtos.Select(d => _normalizer.Normalize(d, columns, now)).ToList();

        await _store.UpsertWorkItemsAsync(items);
        await _store.SetLastSuccessfulPollAsync("workitems", now);
        _debugLog.UpdatePollStatus("workitems", now, now.AddMinutes(appSettings.WorkItemPollingIntervalMinutes), 1);

        PollCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _timer?.Dispose();
}
