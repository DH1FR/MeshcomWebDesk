namespace MeshcomWebClient.Models;

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
}
