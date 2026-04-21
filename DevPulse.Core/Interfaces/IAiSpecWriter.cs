using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiSpecWriter
{
    Task<AiFilePaths> WriteAsync(
        string outputRoot,
        string projectSlug,
        int workItemId,
        DateTimeOffset timestampUtc,
        string specMarkdown,
        string promptMarkdown,
        IReadOnlyList<AiAttempt> attemptHistory,
        CancellationToken ct = default);
}
