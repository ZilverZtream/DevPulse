using DevPulse.Core.Enums;
using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiProvider
{
    string Id { get; }
    string DisplayName { get; }
    AiProviderKind Kind { get; }
    AiDataPolicy DataPolicy { get; }
    Task<AiHealthResult> HealthCheckAsync(CancellationToken ct = default);
    Task<AiGenerateResult> GenerateAsync(AiGenerateRequest req, CancellationToken ct = default);
}
