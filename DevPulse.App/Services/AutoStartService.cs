using Microsoft.Win32;

namespace DevPulse.App.Services;

// Manages the per-user Run-key entry that launches DevPulse at sign-in.
// Per-user (HKCU) avoids requiring elevation and keeps the entry tied to the current Windows account.
public static class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DevPulse";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "AutoStartService: IsEnabled failed");
            return false;
        }
    }

    public static void Enable(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                Serilog.Log.Warning("AutoStartService: Run key could not be opened or created");
                return;
            }
            // Quote the path so spaces (e.g. "Program Files") survive the shell parse.
            key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "AutoStartService: Enable failed");
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;
            if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "AutoStartService: Disable failed");
        }
    }
}
