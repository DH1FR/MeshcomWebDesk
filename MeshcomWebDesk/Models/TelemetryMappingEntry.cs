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
    /// Optional weather/sensor role. Drives two independent things:
    /// - "temp"/"humidity"/"pressure" feed the map popup (<see cref="Services.MeshcomUdpService"/>
    ///   sets Status.OwnTemp/OwnHumidity/OwnPressure for these three roles).
    /// - Any of the 7 roles below additionally selects which fixed field this value is sent
    ///   as in the native extudp "tele" telegram (see <see cref="MeshcomSettings.TelemetryExtUdpEnabled"/>),
    ///   when <see cref="MeshcomSettings.TelemetryExtUdpEnabled"/> is on. Empty = not sent via extudp.
    /// Allowed values: "temp", "humidity", "pressure", "temp2", "qnh", "gasres", "co2", or empty.
    /// For "temp"/"humidity"/"pressure" this takes precedence over unit-based auto-detection
    /// (used for the map popup only, for entries configured before this field existed).
    /// At most one mapping entry should use a given role; the UI enforces this for the extudp
    /// path (mirrors a radio group, like the former ExtUdpSlot mechanism it replaces).
    /// </summary>
    public string WeatherRole { get; set; } = string.Empty;
}
