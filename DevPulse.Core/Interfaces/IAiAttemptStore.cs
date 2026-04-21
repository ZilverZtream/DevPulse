using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiAttemptStore
{
    Task RecordAttemptAsync(AiAttempt attempt, CancellationToken ct = default);
    Task<IReadOnlyList<AiAttempt>> GetAttemptsForWorkItemAsync(int workItemId, CancellationToken ct = default);
}
