using System.Text.Json;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Background service that persists the runtime state (chat tabs, MH list, monitor feed)
/// to a JSON file on disk. Loads on startup, saves every 5 minutes and on graceful shutdown.
/// </summary>
public class DataPersistenceService : BackgroundService
{
    private const string FileName = "meshcom-state.json";
    private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MhPurgeInterval  = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ChatService _chatService;
    private readonly MeshcomUdpService _udpService;
    private readonly MeshcomSettings _settings;
    private readonly ILogger<DataPersistenceService> _logger;

    public DataPersistenceService(
        ChatService chatService,
        MeshcomUdpService udpService,
        IOptions<MeshcomSettings> settings,
        ILogger<DataPersistenceService> logger)
    {
        _chatService = chatService;
        _udpService  = udpService;
        _settings    = settings.Value;
        _logger      = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadAsync();

        // Run an immediate purge after loading so stale entries from the snapshot
        // are removed right away (important when the setting was just configured).
        int startupRemoved = _chatService.PurgeMhListByAge();
        if (startupRemoved > 0)
            _logger.LogInformation("MH list startup purge removed {Count} stale entries", startupRemoved);

        var lastPurge = DateTime.Now;
        using var timer = new PeriodicTimer(AutoSaveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (DateTime.Now - lastPurge >= MhPurgeInterval)
                {
                    int removed = _chatService.PurgeMhListByAge();
                    if (removed > 0)
                        _logger.LogInformation("MH list purge removed {Count} stale entries", removed);
                    lastPurge = DateTime.Now;
                }
                await SaveAsync();
            }
        }
        catch (OperationCanceledException) { }

        // Final save on graceful shutdown
        await SaveAsync();
    }

    /// <summary>Saves the current state to disk. Called periodically and on shutdown.</summary>
    public async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(_settings.DataPath);
            var path     = FilePath();
            var snapshot = _chatService.CreateSnapshot();
            snapshot.OwnLatitude       = _udpService.Status.OwnLatitude;
            snapshot.OwnLongitude      = _udpService.Status.OwnLongitude;
            snapshot.OwnAltitude       = _udpService.Status.OwnAltitude;
            snapshot.OwnPositionSource = _udpService.Status.OwnPositionSource;
            snapshot.NodeHwId          = _udpService.Status.NodeHwId;
            snapshot.NodeFirmware      = _udpService.Status.NodeFirmware;
            var json     = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            _logger.LogDebug("State saved to {Path} ({Tabs} tabs, {Mh} MH entries, {Mon} monitor entries)",
                path, snapshot.Tabs.Count, snapshot.MhList.Count, snapshot.MonitorMessages.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state to {Path}", FilePath());
        }
    }

    private async Task LoadAsync()
    {
        var path = FilePath();
        if (!File.Exists(path))
        {
            _logger.LogInformation("No saved state found at {Path} – starting fresh", path);
            return;
        }

        try
        {
            var json     = await File.ReadAllTextAsync(path);
            var snapshot = JsonSerializer.Deserialize<PersistenceSnapshot>(json);
            if (snapshot is not null)
            {
                // Migration: set default LastSrcType for stations persisted before this field was introduced
                foreach (var s in snapshot.MhList.Where(s => s.LastSrcType == null))
                    s.LastSrcType = "lora";

                _chatService.LoadSnapshot(snapshot);

                if (snapshot.OwnLatitude.HasValue && snapshot.OwnLongitude.HasValue)
                {
                    _udpService.SetOwnPosition(
                        snapshot.OwnLatitude.Value,
                        snapshot.OwnLongitude.Value,
                        snapshot.OwnAltitude,
                        snapshot.OwnPositionSource);
                }

                if (snapshot.NodeHwId.HasValue || !string.IsNullOrEmpty(snapshot.NodeFirmware))
                {
                    _udpService.Status.NodeHwId     = snapshot.NodeHwId;
                    _udpService.Status.NodeFirmware = snapshot.NodeFirmware;
                }

                _logger.LogInformation(
                    "State loaded from {Path} (saved {SavedAt:yyyy-MM-dd HH:mm:ss}, " +
                    "{Tabs} tabs, {Mh} MH entries, {Mon} monitor entries)",
                    path, snapshot.SavedAt,
                    snapshot.Tabs.Count, snapshot.MhList.Count, snapshot.MonitorMessages.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load state from {Path} – starting fresh", path);
        }
    }

    private string FilePath() => Path.Combine(_settings.DataPath, FileName);
}
