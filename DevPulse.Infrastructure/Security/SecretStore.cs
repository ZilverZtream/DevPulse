using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace DevPulse.Infrastructure.Security;

public static class SecretStore
{
    private const string Prefix = "DevPulse_PAT_";

    public static void SavePat(string pat)
    {
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(pat),
            Encoding.UTF8.GetBytes(Prefix),
            DataProtectionScope.CurrentUser);

        var path = GetStoragePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, encrypted);
    }

    public static PatLoadResult TryLoadPat()
    {
        var path = GetStoragePath();
        if (!File.Exists(path)) return PatLoadResult.Missing;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes(Prefix), DataProtectionScope.CurrentUser);
            return PatLoadResult.Ok(Encoding.UTF8.GetString(decrypted));
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            Log.Warning(ex, "SecretStore: PAT decryption failed — key rotation or file corruption");
            return PatLoadResult.Unreadable;
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "SecretStore: PAT file read failed");
            return PatLoadResult.Unreadable;
        }
    }

    public static string? LoadPat() => TryLoadPat().Value;

    public static void ClearPat()
    {
        var path = GetStoragePath();
        if (File.Exists(path)) File.Delete(path);
    }

    private static string GetStoragePath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevPulse", "pat.dpapi");
}
