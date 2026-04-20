namespace DevPulse.Core.Interfaces;

public interface IKvSettings
{
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
}
