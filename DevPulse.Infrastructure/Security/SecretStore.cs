using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Serilog;

namespace DevPulse.Infrastructure.Security;

public static class SecretStore
{
    // ── Named-secret API ──────────────────────────────────────────────

    public static void SaveSecret(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value ?? string.Empty),
            EntropyFor(name),
            DataProtectionScope.CurrentUser);

        var path = GetStoragePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, encrypted);
        TryRestrictAclToCurrentUser(path);
    }

    // Belt-and-braces on top of DPAPI: even though the ciphertext is bound to the current user
    // and unreadable by other accounts, restricting the file's DACL to the current user blocks
    // local-admin or other-user processes from copying the blob off-box and means a future change
    // to a non-DPAPI store wouldn't silently regress to world-readable secrets.
    private static void TryRestrictAclToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var owner = identity.User;
            if (owner is null) return;

            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Strip any rules carried over from the parent's inheritance.
            foreach (FileSystemAccessRule existing in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                security.RemoveAccessRule(existing);

            security.AddAccessRule(new FileSystemAccessRule(
                owner,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // ACL hardening is defense-in-depth on top of DPAPI; if the platform refuses
            // (e.g., FAT32 volume, AV lock), log and leave the file at whatever ACL it inherited.
            Log.Warning(ex, "SecretStore: failed to tighten DACL on {Path}", path);
        }
    }

    public static PatLoadResult TryLoadSecret(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var path = GetStoragePath(name);
        if (!File.Exists(path)) return PatLoadResult.Missing;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, EntropyFor(name), DataProtectionScope.CurrentUser);
            return PatLoadResult.Ok(Encoding.UTF8.GetString(decrypted));
        }
        catch (CryptographicException ex)
        {
            Log.Warning(ex, "SecretStore: {Name} decryption failed", name);
            return PatLoadResult.Unreadable;
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "SecretStore: {Name} file read failed", name);
            return PatLoadResult.Unreadable;
        }
    }

    public static void ClearSecret(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var path = GetStoragePath(name);
        if (File.Exists(path)) File.Delete(path);
    }

    // ── Backward-compat PAT wrappers ─────────────────────────────────

    public static void SavePat(string pat) => SaveSecret("pat", pat);
    public static PatLoadResult TryLoadPat() => TryLoadSecret("pat");
    public static string? LoadPat() => TryLoadPat().Value;
    public static void ClearPat() => ClearSecret("pat");

    // ── Helpers ──────────────────────────────────────────────────────

    // DPAPI entropy per-secret — derived from name to preserve separation between secrets on the same machine.
    // PAT secret keeps its historical "DevPulse_PAT_" entropy for backward compatibility with existing DPAPI blobs.
    private static byte[] EntropyFor(string name)
    {
        var entropyString = name.Equals("pat", StringComparison.OrdinalIgnoreCase)
            ? "DevPulse_PAT_"
            : $"DevPulse_{name.ToUpperInvariant()}_";
        return Encoding.UTF8.GetBytes(entropyString);
    }

    private static string GetStoragePath(string name)
    {
        var fileName = $"{name.ToLowerInvariant()}.dpapi";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevPulse", fileName);
    }
}
