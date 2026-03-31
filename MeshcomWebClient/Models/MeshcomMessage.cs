namespace MeshcomWebClient.Models;

public class MeshcomMessage
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Sender callsign (first hop only).</summary>
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

    /// <summary>True when this packet is a telemetry message (type "tele").</summary>
    public bool IsTelemetry { get; set; }

    /// <summary>
    /// True when this is an APRS-style message acknowledgement (text matches "&lt;call&gt; :ack&lt;id&gt;").
    /// ACKs are shown in the monitor only and do not open or update a chat tab.
    /// </summary>
    public bool IsAck { get; set; }

    /// <summary>True if the message is a broadcast (destination "*" or "CQCQCQ").</summary>
    public bool IsBroadcast =>
        string.Equals(To, "*", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(To, "CQCQCQ", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Unique message ID from the JSON "msg_id" field (hex string, e.g. "69C84664").
    /// Primary key for deduplication.
    /// </summary>
    public string? MsgId { get; set; }

    /// <summary>
    /// Sequence number extracted from the trailing {NNN} marker in message text.
    /// Used to match node echoes and incoming ACKs.
    /// </summary>
    public string? SequenceNumber { get; set; }

    /// <summary>True once a delivery ACK for this outgoing message has been received.</summary>
    public bool IsAcknowledged { get; set; }

    /// <summary>
    /// Full relay path from the "src" field (e.g. "OE1XAR-62,DB0TAW-13,DB0KH-11").
    /// Null when no relay occurred.
    /// </summary>
    public string? RelayPath { get; set; }

    /// <summary>Source type from the JSON "src_type" field: "lora", "udp", or "node".</summary>
    public string? SrcType { get; set; }

    /// <summary>Hardware ID from the JSON "hw_id" field.</summary>
    public int? HwId { get; set; }

    /// <summary>Battery level in percent from the JSON "batt" field.</summary>
    public int? Battery { get; set; }

    /// <summary>Firmware version string (e.g. "4.35p") built from "firmware" + "fw_sub".</summary>
    public string? Firmware { get; set; }

    // ── Telemetry fields (type:"tele") ──────────────────────────────────────
    public double? Temp1    { get; set; }
    public double? Temp2    { get; set; }
    public double? Humidity { get; set; }
    public double? Pressure { get; set; }  // qnh preferred, qfe fallback
}
