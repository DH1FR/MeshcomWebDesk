namespace MeshcomWebDesk.Models;

/// <summary>
/// Represents a station that has been heard via the MeshCom network.
/// Updated on every received message from that callsign.
/// </summary>
public class HeardStation
{
    /// <summary>Sender callsign.</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Timestamp when this station was heard for the first time.</summary>
    public DateTime FirstHeard { get; set; }

    /// <summary>Timestamp of the most recent received message.</summary>
    public DateTime LastHeard { get; set; }

    /// <summary>Total number of received messages from this station.</summary>
    public int MessageCount { get; set; }

    /// <summary>Destination of the last message ("*", callsign, or group).</summary>
    public string LastDestination { get; set; } = string.Empty;

    /// <summary>Text of the last received message.</summary>
    public string LastMessage { get; set; } = string.Empty;

    /// <summary>RSSI of the last received LoRa frame, if available.</summary>
    public int? LastRssi { get; set; }

    /// <summary>SNR of the last received LoRa frame, if available.</summary>
    public double? LastSnr { get; set; }

    /// <summary>Last known GPS latitude (decimal degrees), null if never received.</summary>
    public double? Latitude { get; set; }

    /// <summary>Last known GPS longitude (decimal degrees), null if never received.</summary>
    public double? Longitude { get; set; }

    /// <summary>Last known altitude in metres, null if never received.</summary>
    public int? Altitude { get; set; }

    /// <summary>Timestamp when the GPS position was last updated.</summary>
    public DateTime? LastPositionTime { get; set; }

    /// <summary>Battery level in percent (from "batt" field in position/telemetry packets).</summary>
    public int? Battery { get; set; }

    /// <summary>Hardware ID (from "hw_id" field).</summary>
    public int? HwId { get; set; }

    /// <summary>Firmware version string (e.g. "4.35p").</summary>
    public string? Firmware { get; set; }

    /// <summary>
    /// Full relay path of the last received packet (e.g. "OE1XAR-62,DL0VBK-12,DB0KH-11").
    /// Index 0 = originating station; subsequent entries = relay nodes.
    /// Null when the station was heard directly without any relay.
    /// </summary>
    public string? LastRelayPath { get; set; }

    /// <summary>Number of relay hops in the last received packet (0 = direct reception).</summary>
    public int HopCount { get; set; }

    /// <summary>
    /// True once a direct (non-relayed) chat message or ACK has been received from this station.
    /// Used to draw a direct-link line on the map. Not set for passively heard beacons.
    /// </summary>
    public bool DirectLinkConfirmed { get; set; }

    /// <summary>
    /// Number of packets received via <see cref="LastRelayPath"/>.
    /// Resets to 1 whenever the relay path changes.
    /// Used to scale relay polyline thickness on the map.
    /// </summary>
    public int RelayPathCount { get; set; }

    // ── Telemetry (last received tele packet) ──────────────────────────────

    /// <summary>Last measured temperature in °C (Temp1 field of the tele packet).</summary>
    public double? Temp1 { get; set; }

    /// <summary>Last measured relative humidity in %.</summary>
    public double? Humidity { get; set; }

    /// <summary>Last measured atmospheric pressure in hPa.</summary>
    public double? Pressure { get; set; }

    /// <summary>
    /// UTC timestamp of the last received telemetry packet.
    /// Used to display data freshness on the map.
    /// </summary>
    public DateTime? LastTelemetryTime { get; set; }
}
