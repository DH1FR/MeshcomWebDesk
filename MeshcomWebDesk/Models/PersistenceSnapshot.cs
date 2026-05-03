namespace MeshcomWebDesk.Models;

/// <summary>
/// Serialisable snapshot of the runtime state persisted to disk between restarts.
/// </summary>
public class PersistenceSnapshot
{
    public DateTime SavedAt { get; set; } = DateTime.Now;

    public List<ChatTab> Tabs { get; set; } = [];

    public List<HeardStation> MhList { get; set; } = [];

    public List<MeshcomMessage> MonitorMessages { get; set; } = [];

    /// <summary>Last known own GPS position, persisted so it is available immediately on restart.</summary>
    public double? OwnLatitude { get; set; }
    public double? OwnLongitude { get; set; }
    public int? OwnAltitude { get; set; }
    public string OwnPositionSource { get; set; } = string.Empty;

    /// <summary>Last known node hardware ID and firmware version, persisted so status bar is populated immediately on restart.</summary>
    public int? NodeHwId { get; set; }
    public string? NodeFirmware { get; set; }
}
