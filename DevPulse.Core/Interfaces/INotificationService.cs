using DevPulse.Core.Models;

namespace DevPulse.Core.Interfaces;

public interface INotificationService
{
    Task ShowAsync(DevOpsEvent devOpsEvent, CancellationToken ct = default);
}
