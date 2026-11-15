using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.Update;

/// <summary>
/// One-click update from the official GitHub release: download → SHA-256 check → backup → file swap → restart.
/// A failed start of the new version is rolled back automatically (see <see cref="UpdateRecovery"/>).
/// </summary>
public sealed class SelfUpdateService
{
    private const string DefaultReleaseUrl = "https://api.github.com/repos/DH1FR/MeshcomWebDesk/releases/latest";
    private const string AllowedDownloadPrefix = "https://github.com/DH1FR/MeshcomWebDesk/releases/download/";
    private const long MaxDownloadBytes = 400L * 1024 * 1024;

    private readonly ILogger<SelfUpdateService> _logger;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateStatus Status { get; } = new();
    public InstallInfo Install { get; } = UpdateRestart.Detect();

    /// <summary>Raised on every progress change (UI refresh).</summary>
    public event Action? OnChange;

    public SelfUpdateService(ILogger<SelfUpdateService> logger, IOptionsMonitor<MeshcomSettings> settings, IConfiguration config)
    {
        _logger = logger;
        _settings = settings;
        _config = config;
    }

    private string DataPath => _settings.CurrentValue.DataPath;
    public string DataPathPublic => DataPath;

    public List<BackupInfo> Backups => FileSwap.ListBackups(DataPath);
    public UpdateResult? LastResult => UpdateRecovery.ReadResult(DataPath);

    private void Set(UpdatePhase phase, int percent = 0, string? error = null)
    {
        Status.Phase = phase;
        Status.Percent = percent;
        Status.Error = error;
        OnChange?.Invoke();
    }

    // ── Update ───────────────────────────────────────────────────────────────────────────

    /// <summary>Runs the whole update. On success the process restarts and this method does not return.</summary>
    public async Task<(bool Ok, string Message)> StartUpdateAsync(CancellationToken ct = default)
    {
        if (!Install.Supported)
            return (false, Install.UnsupportedReason ?? "Updates are not supported for this installation.");
        if (!await _gate.WaitAsync(0, ct))
            return (false, "An update is already running.");

        string? stage = null;
        try
        {
            Set(UpdatePhase.Checking);
            using var http = NewHttp();

            var releaseUrl = _config["Meshcom:UpdateApiUrl"] ?? DefaultReleaseUrl;
            var testOverride = !string.Equals(releaseUrl, DefaultReleaseUrl, StringComparison.Ordinal);

            var release = await http.GetFromJsonAsync<GitHubRelease>(releaseUrl, ct);
            if (release is null || string.IsNullOrEmpty(release.TagName))
                return Fail("Could not read the latest release.");

            if (!Version.TryParse(release.TagName.TrimStart('v'), out var latest))
                return Fail($"Unexpected release tag '{release.TagName}'.");
            var current = Version.Parse(UpdateRecovery.CurrentVersion);
            if (latest <= current)
                return Fail("Already up to date.");

            Status.TargetVersion = latest.ToString(3);

            var assetName = AssetName(release.TagName);
            var asset = release.Assets.FirstOrDefault(a => a.Name == assetName);
            var shaAsset = release.Assets.FirstOrDefault(a => a.Name == assetName + ".sha256");
            if (asset is null)  return Fail($"No release asset for this platform ({assetName}).");
            if (shaAsset is null) return Fail("The release has no SHA-256 checksum for this platform – update refused.");
            if (asset.Size > MaxDownloadBytes) return Fail("Release asset is unreasonably large – update refused.");

            if (!testOverride &&
                (!asset.Url.StartsWith(AllowedDownloadPrefix, StringComparison.Ordinal) ||
                 !shaAsset.Url.StartsWith(AllowedDownloadPrefix, StringComparison.Ordinal)))
                return Fail("Unexpected download location – update refused.");

            stage = Path.Combine(DataPath, "update", "staging");
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            Directory.CreateDirectory(stage);
            var archive = Path.Combine(stage, assetName);

            // ── Download ──
            Set(UpdatePhase.Downloading, 0);
            await DownloadAsync(http, asset.Url, archive, asset.Size, ct);

            // ── Verify ──
            Set(UpdatePhase.Verifying);
            var expected = (await http.GetStringAsync(shaAsset.Url, ct)).Trim().Split(' ', '\t', '\n', '\r')[0].ToLowerInvariant();
            string actual;
            await using (var fs = File.OpenRead(archive))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (expected.Length != 64 || actual != expected)
                return Fail("SHA-256 checksum mismatch – the download was discarded.");

            // ── Extract ──
            Set(UpdatePhase.Extracting);
            var extracted = Path.Combine(stage, "new");
            Directory.CreateDirectory(extracted);
            if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ZipFile.ExtractToDirectory(archive, extracted, overwriteFiles: true);
            else
            {
                await using var gz = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gz, extracted, overwriteFiles: true, ct);
            }
            var root = FlattenSingleRoot(extracted);

            var exeName = OperatingSystem.IsWindows() ? "MeshcomWebDesk.exe" : "MeshcomWebDesk";
            if (!File.Exists(Path.Combine(root, exeName)))
                return Fail("The downloaded package does not contain the application – update refused.");

            // ── Backup ──
            Set(UpdatePhase.BackingUp);
            var backup = FileSwap.CreateBackup(Install.AppDir, DataPath, UpdateRecovery.CurrentVersion);
            _logger.LogInformation("Update: backup created ({Backup})", backup);

            // ── Apply ──
            Set(UpdatePhase.Applying);
            UpdateRecovery.WriteState(DataPath, new UpdateState
            {
                From = UpdateRecovery.CurrentVersion,
                To = latest.ToString(3),
                Backup = backup,
                Attempts = 0,
                StartedUtc = DateTime.UtcNow
            });

            try
            {
                FileSwap.ApplyTree(root, Install.AppDir);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update: applying files failed – restoring backup");
                try { FileSwap.RestoreBackup(Install.AppDir, DataPath, backup); }
                catch (Exception rex) { _logger.LogError(rex, "Update: restore after failed apply failed too"); }
                UpdateRecovery.ClearState(DataPath);
                return Fail("Applying the update failed, the previous version was restored: " + ex.Message);
            }

            try { Directory.Delete(stage, recursive: true); } catch { /* ignore */ }

            // ── Restart ──
            Set(UpdatePhase.Restarting);
            _logger.LogInformation("Update: v{From} → v{To} applied, restarting ({Mode})",
                UpdateRecovery.CurrentVersion, latest.ToString(3), Install.Mode);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);   // let the UI show "restarting"
                UpdateRestart.RestartNow(Install);
            });
            return (true, "Update installed, restarting …");
        }
        catch (OperationCanceledException)
        {
            return Fail("Update cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed");
            return Fail(ex.Message);
        }
        finally
        {
            if (Status.Phase != UpdatePhase.Restarting && stage is not null)
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); } catch { /* ignore */ }
            }
            if (Status.Phase != UpdatePhase.Restarting) _gate.Release();
        }
    }

    // ── Rollback ─────────────────────────────────────────────────────────────────────────

    /// <summary>Restores a backup (program files + data) and restarts.</summary>
    public async Task<(bool Ok, string Message)> RollbackAsync(string backupName)
    {
        if (!Install.Supported)
            return (false, Install.UnsupportedReason ?? "Not supported for this installation.");
        if (!await _gate.WaitAsync(0))
            return (false, "An update is already running.");

        try
        {
            var backup = Backups.FirstOrDefault(b => b.Name == backupName);
            if (backup is null) return (false, "Backup not found.");

            Set(UpdatePhase.BackingUp);
            // Keep the current state as a backup too so the rollback itself can be undone.
            var safety = FileSwap.CreateBackup(Install.AppDir, DataPath, UpdateRecovery.CurrentVersion);

            Set(UpdatePhase.Applying);
            try
            {
                FileSwap.RestoreBackup(Install.AppDir, DataPath, backupName);
            }
            catch
            {
                // Do not leave a half-restored installation behind: put the state from before the rollback back.
                try { FileSwap.RestoreBackup(Install.AppDir, DataPath, safety); } catch { /* reported below */ }
                throw;
            }
            UpdateRecovery.ClearState(DataPath);
            UpdateRecovery.WriteResult(DataPath, new UpdateResult
            {
                Kind = "rollback-manual",
                From = UpdateRecovery.CurrentVersion,
                To = backup.Version,
                WhenUtc = DateTime.UtcNow
            });

            Set(UpdatePhase.Restarting);
            _logger.LogInformation("Update: rolled back to v{Version}, restarting", backup.Version);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                UpdateRestart.RestartNow(Install);
            });
            return (true, "Rollback done, restarting …");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback failed");
            Set(UpdatePhase.Failed, 0, ex.Message);
            _gate.Release();
            return (false, ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private (bool, string) Fail(string message)
    {
        _logger.LogWarning("Update: {Message}", message);
        Set(UpdatePhase.Failed, 0, message);
        return (false, message);
    }

    private string AssetName(string tag)
    {
        var rid = Install.Rid;
        if (rid == "win-x64")
            return Install.SelfContained
                ? $"MeshcomWebDesk-{tag}-win-x64-selfcontained.zip"
                : $"MeshcomWebDesk-{tag}-win-x64.zip";
        return $"MeshcomWebDesk-{tag}-{rid}.tar.gz";
    }

    private HttpClient NewHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MeshcomWebDesk-Updater/" + UpdateRecovery.CurrentVersion);
        return http;
    }

    private async Task DownloadAsync(HttpClient http, string url, string target, long expectedSize, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? expectedSize;

        await using var src = await response.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(target);
        var buffer = new byte[81920];
        long done = 0;
        int lastPercent = -1;
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (done > MaxDownloadBytes) throw new InvalidOperationException("Download exceeds the size limit.");
            var percent = total > 0 ? (int)(done * 100 / total) : 0;
            if (percent != lastPercent) { lastPercent = percent; Set(UpdatePhase.Downloading, percent); }
        }
    }

    /// <summary>Tarballs wrap everything in one top-level folder – return the directory that holds the files.</summary>
    private static string FlattenSingleRoot(string dir)
    {
        var dirs = Directory.GetDirectories(dir);
        var files = Directory.GetFiles(dir);
        return files.Length == 0 && dirs.Length == 1 ? dirs[0] : dir;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")]   public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                 public string Name { get; set; } = "";
        [JsonPropertyName("size")]                 public long Size { get; set; }
        [JsonPropertyName("browser_download_url")] public string Url { get; set; } = "";
    }
}
