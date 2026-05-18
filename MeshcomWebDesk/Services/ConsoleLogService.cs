using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Writes console output lines to daily rolling log files per host/port identifier.
/// File name pattern: console-{host}-{yyyy-MM-dd}.log
/// Old files are purged after <see cref="MeshcomSettings.LogRetainDays"/> days.
/// </summary>
public class ConsoleLogService
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly string _resolvedLogPath;
    private readonly ILogger<ConsoleLogService> _logger;

    // One writer per host key, reused within the same calendar day.
    private readonly Dictionary<string, (StreamWriter Writer, DateOnly Date)> _writers = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ConsoleLogService(
        IOptionsMonitor<MeshcomSettings> settingsMonitor,
        ResolvedLogPath resolvedLogPath,
        ILogger<ConsoleLogService> logger)
    {
        _settingsMonitor  = settingsMonitor;
        _resolvedLogPath  = resolvedLogPath.Path;
        _logger           = logger;
    }

    /// <summary>
    /// Appends <paramref name="line"/> with a UTC timestamp to the console log for <paramref name="host"/>.
    /// A no-op when <paramref name="enabled"/> is false.
    /// </summary>
    public async Task WriteAsync(string host, bool enabled, string line)
    {
        if (!enabled || string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning("ConsoleLogService.WriteAsync: skipped – enabled={Enabled}, host='{Host}'", enabled, host);
            return;
        }

        // Strip leading/trailing whitespace but keep the raw text
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        await _lock.WaitAsync();
        try
        {
            var s            = _settingsMonitor.CurrentValue;
            var today        = DateOnly.FromDateTime(DateTime.Now);
            var logPath      = ResolveLogPath(s);
            var writer       = await GetOrCreateWriterAsync(host, today, logPath);

            await writer.WriteLineAsync(
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {trimmed}");
            await writer.FlushAsync();

            var safeHost = string.Concat(host.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            _logger.LogDebug("ConsoleLogService: wrote to {File}",
                Path.Combine(logPath, $"console-{safeHost}-{today:yyyy-MM-dd}.log"));

            // Purge old files (best-effort, once per write call, guarded by lock)
            PurgeOldFiles(host, today, logPath, s.LogRetainDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConsoleLogService: write failed for host {Host} – logPath={LogPath}",
                host, _settingsMonitor.CurrentValue.LogPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effective log path: prefers the configured value from settings,
    /// falls back to the startup-resolved path (immune to override.json empty string).
    /// </summary>
    private string ResolveLogPath(MeshcomSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.LogPath))
            return s.LogPath;
        return _resolvedLogPath;
    }

    private async Task<StreamWriter> GetOrCreateWriterAsync(string host, DateOnly today, string logPath)
    {
        // Reuse existing writer if same host + same day
        if (_writers.TryGetValue(host, out var entry) && entry.Date == today)
            return entry.Writer;

        // Close stale writer (new day or first call)
        if (_writers.TryGetValue(host, out var old))
        {
            try { await old.Writer.DisposeAsync(); } catch { }
            _writers.Remove(host);
        }

        Directory.CreateDirectory(logPath);

        // Sanitise host for use in filename (replace characters not valid in filenames)
        var safeHost = string.Concat(host.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var fileName = $"console-{safeHost}-{today:yyyy-MM-dd}.log";
        var filePath = Path.Combine(logPath, fileName);

        var writer = new StreamWriter(filePath, append: true, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = false
        };

        _writers[host] = (writer, today);
        return writer;
    }

    private void PurgeOldFiles(string host, DateOnly today, string logPath, int retainDays)
    {
        if (retainDays <= 0 || !Directory.Exists(logPath)) return;

        var safeHost = string.Concat(host.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var cutoff   = today.AddDays(-retainDays);

        try
        {
            foreach (var file in Directory.EnumerateFiles(logPath, $"console-{safeHost}-*.log"))
            {
                var name     = Path.GetFileNameWithoutExtension(file);
                // Extract date part: last 10 chars of "console-{host}-{yyyy-MM-dd}"
                var datePart = name.Length >= 10 ? name[^10..] : string.Empty;
                if (DateOnly.TryParseExact(datePart, "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var fileDate)
                    && fileDate < cutoff)
                {
                    File.Delete(file);
                    _logger.LogDebug("ConsoleLogService: deleted old log {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ConsoleLogService: purge failed for host {Host}", host);
        }
    }
}
