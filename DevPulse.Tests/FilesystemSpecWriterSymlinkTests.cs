using System.Diagnostics;
using DevPulse.Infrastructure.Ai;
using FluentAssertions;

namespace DevPulse.Tests;

public class FilesystemSpecWriterSymlinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"devpulse-fs-link-{Guid.NewGuid():N}");
    private readonly string _outsideTarget = Path.Combine(Path.GetTempPath(), $"devpulse-outside-{Guid.NewGuid():N}");
    private readonly FilesystemSpecWriter _sut = new();

    [Fact]
    public async Task WriteAsync_RefusesWriteThroughDirectoryJunction()
    {
        if (!OperatingSystem.IsWindows())
            return; // Junction creation is Windows-only.

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outsideTarget);

        // The slug FilesystemSpecWriter will derive from "Proj" is "Proj"; place the junction
        // there so the resolved candidate path traverses it.
        var slugDir = Path.Combine(_root, "Proj");

        if (!TryCreateJunction(slugDir, _outsideTarget))
            return; // mklink unavailable on this runner; skip silently.

        var act = () => _sut.WriteAsync(_root, "Proj", 1, DateTimeOffset.UtcNow,
            "## s\nb", "p", []);

        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*reparse point*");
    }

    private static bool TryCreateJunction(string linkPath, string targetPath)
    {
        try
        {
            // Use cmd's mklink /J — it doesn't require admin privileges (unlike symlinks).
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
        try { if (Directory.Exists(_outsideTarget)) Directory.Delete(_outsideTarget, recursive: true); } catch { }
    }
}
