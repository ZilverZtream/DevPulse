using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

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

    public static string? LoadPat()
    {
        var path = GetStoragePath();
        if (!File.Exists(path)) return null;

        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(
                encrypted,
                Encoding.UTF8.GetBytes(Prefix),
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }

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
