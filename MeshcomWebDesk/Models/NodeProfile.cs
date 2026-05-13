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

    /// <summary>
    /// Local UDP port to listen on for packets from this device.
    /// <para>
    /// MeshCom nodes typically use port 1799 and this <b>cannot be changed</b> on the device.
    /// When multiple nodes share the same port, incoming packets are distinguished by the
    /// source IP address of each node (<see cref="DeviceIp"/>) rather than by port number.
    /// This field is kept for completeness and for setups where port forwarding is used.
    /// </para>
    /// </summary>
    public int ListenPort { get; set; } = 1799;

    /// <summary>
    /// When true this is the primary node.
    /// All features (map, MH list, bot, telemetry, …) use this node exclusively.
    /// Exactly one node in the list must be primary.
    /// </summary>
    public bool IsPrimary { get; set; } = false;

    // ── TLS Console ──────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 fingerprint (colon-separated hex, e.g. "AA:BB:CC:…") of the node's
    /// self-signed TLS certificate.  Empty = first-connect mode: accept any cert and
    /// present the fingerprint for confirmation in the Settings page.
    /// Each MeshCom node generates its own unique EC P-256 key + cert on first boot,
    /// so every node will have a different fingerprint.
    /// </summary>
    public string TelnetCertThumbprint { get; set; } = string.Empty;

    /// <summary>
    /// Password sent to the node after the TLS handshake (max. 14 chars, set via
    /// <c>--passwd</c> on the node console).  Empty = no authentication required.
    /// </summary>
    public string TelnetPassword { get; set; } = string.Empty;
}
