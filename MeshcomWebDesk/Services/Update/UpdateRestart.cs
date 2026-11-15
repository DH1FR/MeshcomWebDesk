using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace MeshcomWebDesk.Services.Update;

/// <summary>Detects the install type and restarts the process after files were replaced.</summary>
internal static class UpdateRestart
{
    /// <summary>Exit code used to make a supervisor (service recovery / systemd) restart us.</summary>
    public const int RestartExitCode = 75;

    private const string WindowsServiceName = "MeshcomWebDesk";

    // ── Detection ────────────────────────────────────────────────────────────────────────

    public static InstallInfo Detect()
    {
        var appDir  = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exePath = Environment.ProcessPath ?? "";
        var exeName = Path.GetFileNameWithoutExtension(exePath);

        var rid = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
                  RuntimeInformation.ProcessArchitecture switch
                  {
                      Architecture.X64   => "x64",
                      Architecture.Arm64 => "arm64",
                      var a              => a.ToString().ToLowerInvariant()
                  };

        var selfContained = typeof(UpdateRestart).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SelfContained")?.Value
            ?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        InstallMode mode;
        string? reason = null;

        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            mode = InstallMode.Docker;
            reason = "Docker installations are updated by pulling a new image.";
        }
        else if (!exeName.Equals("MeshcomWebDesk", StringComparison.OrdinalIgnoreCase) ||
                 appDir.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "Debug", StringComparison.OrdinalIgnoreCase) ||
                 appDir.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar + "Release", StringComparison.OrdinalIgnoreCase))
        {
            mode = InstallMode.Development;
            reason = "Development run (not an installed release).";
        }
        else if (OperatingSystem.IsWindows() && Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
            mode = InstallMode.WindowsService;
        else if (OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID")))
            mode = InstallMode.Systemd;
        else
            mode = InstallMode.Manual;

        // Linux/macOS release archives are framework-dependent only.
        if (reason is null && selfContained && !OperatingSystem.IsWindows())
            reason = "Self-contained builds are only updated on Windows.";

        if (reason is null && !IsWritable(appDir))
            reason = $"The program directory is not writable ({appDir}).";

        return new InstallInfo(mode, appDir, exePath, rid, selfContained, reason is null, reason);
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N")[..6]);
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // ── Restart ──────────────────────────────────────────────────────────────────────────

    /// <summary>Terminates the process so that it comes back with the new files. Does not return.</summary>
    public static void RestartNow(InstallInfo info)
    {
        try
        {
            switch (info.Mode)
            {
                case InstallMode.WindowsService:
                    // Service recovery (sc failure …) restarts an abnormally terminated service; the delayed
                    // "sc start" is a safety net for services without recovery actions (fails harmlessly
                    // with "already running" otherwise).
                    SpawnShell($"ping -n 8 127.0.0.1 >nul & sc start {WindowsServiceName}");
                    break;

                case InstallMode.Systemd:
                    break;   // Restart=on-failure + non-zero exit code

                default:
                    SpawnRelaunch(info.ExePath);
                    Log.CloseAndFlush();
                    Environment.Exit(0);
                    return;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update: could not schedule restart helper");
        }

        Log.CloseAndFlush();
        Environment.Exit(RestartExitCode);
    }

    private static void SpawnShell(string command)
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
        {
            UseShellExecute = false,
            CreateNoWindow  = true
        };
        Process.Start(psi);
    }

    private static void SpawnRelaunch(string exePath)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & start \"\" \"{exePath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow  = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            };
        }
        else
        {
            psi = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"(sleep 3; exec \"{exePath}\") >/dev/null 2>&1 &");
        }
        Process.Start(psi);
    }
}
