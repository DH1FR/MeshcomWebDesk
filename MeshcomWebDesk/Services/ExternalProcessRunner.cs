using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Launches an external process from <see cref="MeshcomSettings.BotExternalCommandsPath"/>,
/// writes a JSON payload to its stdin, and returns whatever the process writes to stdout.
/// Used by bot commands with <see cref="BotCommandEntry.IsExternal"/> = true.
/// Shared infrastructure — also intended for external calendar-beacon text generation (not yet implemented).
/// </summary>
public sealed class ExternalProcessRunner
{
    private static readonly HashSet<string> SupportedExtensions =
        [".exe", ".ps1", ".bat", ".cmd", ".py"];

    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<ExternalProcessRunner> _logger;

    public ExternalProcessRunner(
        IOptionsMonitor<MeshcomSettings> settings,
        ILogger<ExternalProcessRunner> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>
    /// Launches the named file from the configured external-commands directory,
    /// passes <paramref name="jsonPayload"/> on stdin (UTF-8), and returns trimmed stdout.
    /// Returns <c>null</c> when the process produces no output, exits with a non-zero code,
    /// or times out.
    /// </summary>
    public async Task<string?> RunAsync(string fileName, string jsonPayload, int timeoutSeconds)
    {
        var dir = _settings.CurrentValue.BotExternalCommandsPath;

        if (string.IsNullOrWhiteSpace(dir))
        {
            _logger.LogWarning("ExternalProcessRunner: BotExternalCommandsPath is not configured.");
            return null;
        }

        // Security: reject file names that contain path separators or parent-dir components
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains(".."))
        {
            _logger.LogWarning("ExternalProcessRunner: invalid file name rejected: {FileName}", fileName);
            return null;
        }

        var fullPath   = Path.GetFullPath(Path.Combine(dir, fileName));
        var dirNormed  = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(dirNormed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("ExternalProcessRunner: path-traversal attempt rejected: {Path}", fullPath);
            return null;
        }

        if (!File.Exists(fullPath))
        {
            _logger.LogWarning("ExternalProcessRunner: file not found: {Path}", fullPath);
            return null;
        }

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
        {
            _logger.LogWarning("ExternalProcessRunner: unsupported extension '{Ext}' for {File}", ext, fileName);
            return null;
        }

        var psi = BuildStartInfo(fullPath, ext, dir);

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null.");

            await process.StandardInput.WriteAsync(jsonPayload);
            process.StandardInput.Close();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                _logger.LogWarning(
                    "ExternalProcessRunner: [{File}] timed out after {Timeout}s.",
                    fileName, timeoutSeconds);
                return null;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogWarning("ExternalProcessRunner: [{File}] stderr: {Stderr}", fileName, stderr.Trim());

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "ExternalProcessRunner: [{File}] exited with code {Code}.",
                    fileName, process.ExitCode);
                return null;
            }

            var result = stdout.Trim();
            return result.Length == 0 ? null : result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExternalProcessRunner: failed to run [{File}].", fileName);
            return null;
        }
    }

    private static ProcessStartInfo BuildStartInfo(string fullPath, string ext, string workingDir)
    {
        ProcessStartInfo psi = ext switch
        {
            ".ps1"        => new ProcessStartInfo("powershell.exe",
                                 $"-NonInteractive -ExecutionPolicy Bypass -File \"{fullPath}\""),
            ".bat" or ".cmd" => new ProcessStartInfo("cmd.exe", $"/c \"{fullPath}\""),
            ".py"         => new ProcessStartInfo("python", $"\"{fullPath}\""),
            _             => new ProcessStartInfo(fullPath),
        };

        psi.UseShellExecute            = false;
        psi.RedirectStandardInput      = true;
        psi.RedirectStandardOutput     = true;
        psi.RedirectStandardError      = true;
        psi.StandardInputEncoding      = Encoding.UTF8;
        psi.StandardOutputEncoding     = Encoding.UTF8;
        psi.StandardErrorEncoding      = Encoding.UTF8;
        psi.WorkingDirectory           = workingDir;

        return psi;
    }

    /// <summary>
    /// Serialises the standard bot-command JSON payload that is written to the external
    /// process stdin. Exposed as a static helper so callers can unit-test the payload shape.
    /// </summary>
    public static string BuildBotPayload(
        string commandName,
        string[] args,
        string senderCallsign,
        MeshcomMessage? context,
        MeshcomSettings settings)
    {
        var payload = new
        {
            command = commandName,
            args,
            sender  = senderCallsign,
            message = context is null ? null : (object)new
            {
                rssi      = context.Rssi,
                snr       = context.Snr,
                route     = context.RelayPath,
                hops      = context.RelayPath?.Split(',').Length,
                srcType   = context.SrcType,
                timestamp = context.Timestamp,
                lat       = context.Latitude,
                lon       = context.Longitude,
                alt       = context.Altitude,
            },
            station = new
            {
                myCall         = settings.MyCallsign,
                version        = System.Reflection.Assembly
                                     .GetExecutingAssembly()
                                     .GetName().Version?.ToString(3),
                txPowerDbm     = settings.TxPowerDbm,
                frequencyMhz   = settings.FrequencyMhz,
                antennaGainDbi = settings.AntennaGainDbi,
                antennaType    = settings.AntennaType,
                antennaHeightM = settings.AntennaHeightM,
            },
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented          = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }
}
