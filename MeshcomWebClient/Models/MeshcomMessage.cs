namespace MeshcomWebClient.Models;

public class MeshcomMessage
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Sender callsign.</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>Destination callsign, group name, or "*" for broadcast.</summary>
    public string To { get; set; } = string.Empty;

    /// <summary>Message text content.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>True if this message was sent by us.</summary>
    public bool IsOutgoing { get; set; }

    /// <summary>Raw UDP data as received.</summary>
    public string RawData { get; set; } = string.Empty;

    /// <summary>
    /// Returns the conversation partner (the other side).
    /// For outgoing messages this is the destination, for incoming the sender.
    /// </summary>
    public string ConversationPartner => IsOutgoing ? To : From;

    /// <summary>RSSI in dBm from the LoRa layer, if present in the received JSON.</summary>
    public int? Rssi { get; set; }

    /// <summary>SNR in dB from the LoRa layer, if present in the received JSON.</summary>
    public double? Snr { get; set; }

    /// <summary>GPS latitude in decimal degrees, if provided by the sending station.</summary>
    public double? Latitude { get; set; }

    /// <summary>GPS longitude in decimal degrees, if provided by the sending station.</summary>
    public double? Longitude { get; set; }

    /// <summary>Altitude in metres above sea level, if provided by the sending station.</summary>
    public int? Altitude { get; set; }

    /// <summary>True when this packet is a pure position beacon (type "pos") with no chat text.</summary>
    public bool IsPositionBeacon { get; set; }

    /// <summary>True if the message is a broadcast (destination "*" or "CQCQCQ").</summary>
    public bool IsBroadcast =>
        string.Equals(To, "*", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(To, "CQCQCQ", StringComparison.OrdinalIgnoreCase);
}
