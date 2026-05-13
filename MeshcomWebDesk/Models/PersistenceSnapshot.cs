namespace MeshcomWebDesk.Models;

/// <summary>
/// Per-node portion of a persistence snapshot (tabs + monitor only; MH is primary-only).
/// </summary>
public class NodeSnapshotEntry
{
    public List<ChatTab>        Tabs            { get; set; } = [];
    public List<MeshcomMessage> MonitorMessages { get; set; } = [];
}

/// <summary>
/// Serialisable snapshot of the runtime state persisted to disk between restarts.
/// </summary>
public class PersistenceSnapshot
{
    public DateTime SavedAt { get; set; } = DateTime.Now;

    // ── Primary-node legacy fields (kept for backwards compatibility) ────
    public List<ChatTab>        Tabs            { get; set; } = [];
    public List<HeardStation>   MhList          { get; set; } = [];
    public List<MeshcomMessage> MonitorMessages { get; set; } = [];

    // ── Multi-node state: key = NodeProfile.Id.ToString() ───────────────
    /// <summary>
    /// Tabs and monitor entries for every configured node, keyed by NodeProfile.Id (string form).
    /// When present, this takes precedence over the legacy Tabs/MonitorMessages fields for each node.
    /// </summary>
    public Dictionary<string, NodeSnapshotEntry> NodeSnapshots { get; set; } = [];

    /// <summary>Last known own GPS position, persisted so it is available immediately on restart.</summary>
    public double? OwnLatitude { get; set; }
    public double? OwnLongitude { get; set; }
    public int? OwnAltitude { get; set; }
    public string OwnPositionSource { get; set; } = string.Empty;

    /// <summary>Last known node hardware ID and firmware version, persisted so status bar is populated immediately on restart.</summary>
    public int? NodeHwId { get; set; }
    public string? NodeFirmware { get; set; }
}
