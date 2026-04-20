using Serilog;

namespace DevPulse.App;

internal static class TaskExtensions
{
    internal static void FireAndForget(this Task task, string opName)
    {
        task.ContinueWith(
            t => Log.Error(t.Exception!.GetBaseException(), "{Op} failed", opName),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
