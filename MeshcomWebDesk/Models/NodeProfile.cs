namespace MeshcomWebDesk.Models;

/// <summary>
/// Configuration for a single MeshCom node (device).
/// One node is marked as <see cref="IsPrimary"/> and drives all features
/// (map, MH list, bot, telemetry, beacon, …).
/// Additional nodes are chat-only and visible only when explicitly selected.
/// </summary>
public class NodeProfile
{
    /// <summary>Stable identifier – never changes after creation.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Human-readable display name shown in the node switcher (e.g. "Balkon", "Auto").</summary>
    public string Name { get; set; } = "Node";

    /// <summary>Own callsign (with SSID) used as sender for this node's outgoing messages (e.g. "OE1ABC-1").</summary>
    public string Callsign { get; set; } = "NOCALL-1";

    /// <summary>IP address of the MeshCom device to send UDP messages to.</summary>
    public string DeviceIp { get; set; } = "192.168.1.60";

    /// <summary>UDP port on the MeshCom device.</summary>
    public int DevicePort { get; set; } = 1799;

    /// <summary>IP address to bind the local UDP listener to ("0.0.0.0" = all interfaces).</summary>
    public string ListenIp { get; set; } = "0.0.0.0";

    /// <summary>Local UDP port to listen on for packets from this device.</summary>
    public int ListenPort { get; set; } = 1799;

    /// <summary>
    /// When true this is the primary node.
    /// All features (map, MH list, bot, telemetry, …) use this node exclusively.
    /// Exactly one node in the list must be primary.
    /// </summary>
    public bool IsPrimary { get; set; } = false;
}
