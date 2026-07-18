namespace MeshcomWebDesk.Models;

public class TelemetryMappingEntry
{
    /// <summary>Key in the external JSON file (e.g. "aussentemperatur", "pv_leistung").</summary>
    public string JsonKey { get; set; } = string.Empty;

    /// <summary>Display label used in the telemetry message (e.g. "temp.out", "PV").</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Unit appended to the value (e.g. "°C", "kW", "hPa").</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Number of decimal places to display. Default is 1.</summary>
    public int Decimals { get; set; } = 1;

    /// <summary>
    /// Optional weather role for the map popup.
    /// Allowed values: "temp", "humidity", "pressure", or empty (no role).
    /// Takes precedence over unit-based auto-detection.
    /// </summary>
    public string WeatherRole { get; set; } = string.Empty;

    /// <summary>
    /// Optional position (1-4) of this value in the native extudp "tele" telegram's
    /// values/parm/unit lists. 0 means the entry is not sent via extudp.
    /// Must be unique across all mapping entries; the UI enforces this.
    /// </summary>
    public int ExtUdpSlot { get; set; } = 0;
}
