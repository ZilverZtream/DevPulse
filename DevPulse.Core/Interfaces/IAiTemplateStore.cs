using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface IAiTemplateStore
{
    Task<List<AiTemplate>> GetTemplatesAsync(CancellationToken ct = default);
    Task SaveTemplatesAsync(List<AiTemplate> templates, CancellationToken ct = default);
    Task<AiTemplate?> GetDefaultTemplateForAsync(string workItemType, CancellationToken ct = default);
}
