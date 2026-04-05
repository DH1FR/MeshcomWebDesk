namespace MeshcomWebDesk.Models;

/// <summary>
/// Live status of the UDP connection and message statistics, updated by MeshcomUdpService.
/// </summary>
public class ConnectionStatus
{
    /// <summary>True while the UDP socket is bound and listening.</summary>
    public bool IsListening { get; set; }

    /// <summary>True after the registration packet was successfully sent to the device.</summary>
    public bool IsRegistered { get; set; }

    /// <summary>Timestamp of the last received UDP packet.</summary>
    public DateTime? LastRxTime { get; set; }

    /// <summary>Callsign of the last received message sender.</summary>
    public string LastRxFrom { get; set; } = string.Empty;

    /// <summary>Timestamp of the last sent message.</summary>
    public DateTime? LastTxTime { get; set; }

    /// <summary>Total number of received chat messages since startup.</summary>
    public int RxCount { get; set; }

    /// <summary>Total number of sent messages since startup.</summary>
    public int TxCount { get; set; }

    /// <summary>RSSI (dBm) of the last received LoRa message, if available.</summary>
    public int? LastRssi { get; set; }

    /// <summary>SNR (dB) of the last received LoRa message, if available.</summary>
    public double? LastSnr { get; set; }

    /// <summary>Own GPS latitude – from browser geolocation or node position beacon.</summary>
    public double? OwnLatitude { get; set; }

    /// <summary>Own GPS longitude – from browser geolocation or node position beacon.</summary>
    public double? OwnLongitude { get; set; }

    /// <summary>Own altitude in metres, if available.</summary>
    public int? OwnAltitude { get; set; }

    /// <summary>Source of the own position: "Browser GPS", "Node" or empty when unknown.</summary>
    public string OwnPositionSource { get; set; } = string.Empty;

    /// <summary>True while the beacon is configured and running (BeaconEnabled + group + text set).</summary>
    public bool BeaconActive { get; set; }

    /// <summary>Scheduled time of the next beacon transmission. Null when beacon is inactive.</summary>
    public DateTime? BeaconNextSend { get; set; }

    /// <summary>True while telemetry is configured and active.</summary>
    public bool TelemetryActive { get; set; }

    /// <summary>Scheduled time of the next telemetry transmission. Null when telemetry is inactive.</summary>
    public DateTime? TelemetryNextSend { get; set; }
}
