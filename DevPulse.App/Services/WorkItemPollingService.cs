using DevPulse.Core.Interfaces;
using DevPulse.Core.Services;

namespace DevPulse.App.Services;

public sealed class WorkItemPollingService : PollingLoopBase
{
    private readonly IWorkItemClient _client;
    private readonly IStateStore _store;
    private readonly SettingsService _settings;
    private readonly WorkItemNormalizer _normalizer = new();
    private readonly DebugLogService _debugLog;

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

    protected override string TrackName => "workitems";

    protected override async Task ExecutePollAsync(CancellationToken ct)
    {
        var appSettings = await _settings.GetAppSettingsAsync();
        var columns = await _settings.GetBoardColumnsAsync();
        var now = DateTimeOffset.UtcNow;

        var dtos = await _client.GetWorkItemsAsync(appSettings.AreaPath, appSettings.IterationPath, ct);
        var items = dtos.Select(d => _normalizer.Normalize(d, columns, now)).ToList();

        await _store.UpsertWorkItemsAsync(items, ct);
        await _store.SetLastSuccessfulPollAsync("workitems", now, ct);
        _debugLog.UpdatePollStatus("workitems", now, now.AddMinutes(appSettings.WorkItemPollingIntervalMinutes), 1);
    }

    protected override async Task OnPollFailedAsync(Exception ex, CancellationToken ct)
    {
        _debugLog.UpdatePollStatus("workitems", await _store.GetLastSuccessfulPollAsync("workitems", ct), null, 0, ex.Message);
    }
}
