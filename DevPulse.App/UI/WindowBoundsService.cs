using System.Text.Json;
using System.Text.Json.Serialization;
using DevPulse.App.Services;
using DevPulse.Core.Interfaces;
using Serilog;

namespace DevPulse.App.UI;

public interface IWindowBoundsService
{
    Task<WindowBoundsRecord?> LoadAsync(string key, CancellationToken ct = default);
    Task SaveAsync(string key, WindowBoundsRecord record, CancellationToken ct = default);
}

public sealed record WindowBoundsRecord(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("w")] int Width,
    [property: JsonPropertyName("h")] int Height,
    [property: JsonPropertyName("maximized")] bool Maximized);

public sealed class WindowBoundsService : IWindowBoundsService
{
    public const string BoardFormKey = "window.bounds.BoardForm";
    public const string DebugWindowKey = "window.bounds.DebugWindow";
    public const string InboxEventsFormKey = "window.bounds.InboxEventsForm";

    private static readonly JsonSerializerOptions Json = new();

    private readonly Func<string, CancellationToken, Task<string?>> _read;
    private readonly Func<string, string, CancellationToken, Task> _write;

    public WindowBoundsService(IStateStore store)
    {
        _read = store.GetSettingAsync;
        _write = store.SetSettingAsync;
    }

    // Allows DebugWindow (which has SettingsService but not IStateStore) to construct the service
    // without taking a new direct dependency on the store.
    public WindowBoundsService(SettingsService settings)
    {
        _read = settings.GetRawSettingAsync;
        _write = settings.SetRawSettingAsync;
    }

    public async Task<WindowBoundsRecord?> LoadAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var json = await _read(key, ct);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<WindowBoundsRecord>(json, Json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowBoundsService.LoadAsync failed for key {Key}", key);
            return null;
        }
    }

    public async Task SaveAsync(string key, WindowBoundsRecord record, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(record, Json);
            await _write(key, json, ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WindowBoundsService.SaveAsync failed for key {Key}", key);
        }
    }

    /// <summary>
    /// Apply a saved record to the form: only when the record is present and overlaps a working area.
    /// Otherwise leave the form's existing CenterScreen default in place.
    /// </summary>
    public static void ApplyOnLoad(Form form, WindowBoundsRecord? record)
    {
        if (record is null) return;
        var rect = new Rectangle(record.X, record.Y, record.Width, record.Height);

        // Reject obviously bogus dimensions before checking screen intersection.
        if (rect.Width < 200 || rect.Height < 150) return;

        // Require the saved rect to overlap at least one screen's working area, otherwise the user
        // unplugged the monitor it was on and we'd hide the window off-screen.
        var anyIntersect = Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect));
        if (!anyIntersect) return;

        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = rect;
        if (record.Maximized) form.WindowState = FormWindowState.Maximized;
    }

    /// <summary>
    /// Capture current bounds as a record. Returns null when minimized so we don't persist a hidden
    /// tray-state geometry. Uses RestoreBounds when maximized so the next launch has a sane
    /// non-maximized rect to fall back to.
    /// </summary>
    public static WindowBoundsRecord? CaptureBounds(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized) return null;

        var maximized = form.WindowState == FormWindowState.Maximized;
        var rect = maximized ? form.RestoreBounds : form.Bounds;
        return new WindowBoundsRecord(rect.X, rect.Y, rect.Width, rect.Height, maximized);
    }
}
