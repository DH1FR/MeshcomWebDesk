namespace MeshcomWebDesk.Models;

/// <summary>
/// How WebDesk talks to a single MeshCom node.
/// </summary>
public enum NodeTransport
{
    /// <summary>
    /// MeshCom EXTUDP: shared UDP socket (port 1799), MeshCom-native JSON,
    /// node identified by source IP. Default.
    /// </summary>
    ExtUdp = 0,

    /// <summary>
    /// KISS/TCP: dedicated TCP connection to the node (default port 8001),
    /// KISS-framed AX.25 UI frames. ESP32 firmware only.
    /// See <c>docs/kiss-mode-analysis.md</c>.
    /// </summary>
    Kiss = 1,
}

/// <summary>
/// Result reported by the node for a frame injected over KISS/TCP
/// (KISS port 15 / type 0xF0 TX-result frame, firmware v1.2+).
/// </summary>
public enum KissTxResult
{
    /// <summary>Node accepted the frame into its TX ring (status 0x01). Not yet delivered.</summary>
    Accepted = 0,

    /// <summary>Rejected: the client callsign base does not match the node's own call (status 0x02).</summary>
    RejectedCallsign = 2,

    /// <summary>Rejected: <c>--kiss tx</c> is off on the node (status 0x03).</summary>
    RejectedTxOff = 3,

    /// <summary>
    /// Rejected: frame / payload not usable (status 0x04) – malformed AX.25, unterminated
    /// address field, or control/PID ≠ 0x03/0xF0 (connected-mode AX.25 is refused).
    /// </summary>
    RejectedFrame = 4,

    /// <summary>Rejected: injection rate limit exceeded (&gt; 8 frames/s, status 0x05).</summary>
    RejectedRateLimit = 5,

    /// <summary>No TX-result frame received within the expected window.</summary>
    NoResponse = 99,
}
