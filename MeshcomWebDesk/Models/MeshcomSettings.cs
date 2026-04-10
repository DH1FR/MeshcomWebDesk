namespace MeshcomWebDesk.Models;

public class MeshcomSettings
{
    public const string SectionName = "Meshcom";

    /// <summary>IP address to bind the UDP listener to (e.g. "0.0.0.0" for all interfaces).</summary>
    public string ListenIp { get; set; } = "0.0.0.0";

    /// <summary>UDP port to listen on for messages coming from the MeshCom device.</summary>
    public int ListenPort { get; set; } = 1799;

    /// <summary>IP address of the MeshCom device to send messages to.</summary>
    public string DeviceIp { get; set; } = "192.168.1.60";

    /// <summary>UDP port on the MeshCom device to send messages to.</summary>
    public int DevicePort { get; set; } = 1799;

    /// <summary>Own callsign used as sender in outgoing messages.</summary>
    public string MyCallsign { get; set; } = "NOCALL-1";

    /// <summary>Directory path for log files.</summary>
    public string LogPath { get; set; } = @"C:\Temp\Logs";

    /// <summary>Number of days to retain log files. Older files are deleted automatically.</summary>
    public int LogRetainDays { get; set; } = 30;

    /// <summary>
    /// When true, every UDP packet (RX and TX) is written to the log file at Information level.
    /// Useful for offline traffic analysis. Default is false.
    /// </summary>
    public bool LogUdpTraffic { get; set; } = false;

    /// <summary>
    /// Maximum number of entries kept in the monitor feed. Oldest entries are dropped first.
    /// Default is 1000.
    /// </summary>
    public int MonitorMaxMessages { get; set; } = 1000;

    /// <summary>
    /// When true, only groups listed in <see cref="Groups"/> automatically get a chat tab.
    /// When false (default), every incoming group message opens a tab regardless of <see cref="Groups"/>.
    /// </summary>
    public bool GroupFilterEnabled { get; set; } = false;

    /// <summary>
    /// Whitelist of group names (e.g. "#OE", "#Test") for which a chat tab is automatically
    /// opened when a message arrives. Only evaluated when <see cref="GroupFilterEnabled"/> is true.
    /// </summary>
    public List<string> Groups { get; set; } = [];

    /// <summary>
    /// Directory path for persistent state storage (chat tabs, MH list, monitor feed).
    /// Data is loaded on startup and saved every 5 minutes and on graceful shutdown.
    /// </summary>
    public string DataPath { get; set; } = @"C:\Temp\MeshcomData";

    /// <summary>
    /// Hours added to incoming timestamps for local time display.
    /// Supports half-hour offsets (e.g. 5.5 for IST).
    /// Set to 0 when the host OS is already in the correct timezone.
    /// Default is 0.
    /// </summary>
    public double TimeOffsetHours { get; set; } = 0;

    /// <summary>
    /// When true, an automatic reply is sent once when a brand-new direct chat tab is
    /// opened by an incoming message (i.e. first contact from a callsign).
    /// Default is false.
    /// </summary>
    public bool AutoReplyEnabled { get; set; } = false;

    /// <summary>
    /// Text sent as the automatic reply. Only used when <see cref="AutoReplyEnabled"/> is true.
    /// </summary>
    public string AutoReplyText { get; set; } =
        "---=== MeshcomWebDesk - https://github.com/DH1FR/MeshcomWebDesk ===---";

    /// <summary>
    /// When true, a beacon message is sent periodically to <see cref="BeaconGroup"/>.
    /// Default is false.
    /// </summary>
    public bool BeaconEnabled { get; set; } = false;

    /// <summary>
    /// Target group for the beacon (e.g. "#262"). The leading '#' is stripped before sending.
    /// Only used when <see cref="BeaconEnabled"/> is true.
    /// </summary>
    public string BeaconGroup { get; set; } = string.Empty;

    /// <summary>
    /// Text transmitted as the beacon. Only used when <see cref="BeaconEnabled"/> is true.
    /// </summary>
    public string BeaconText { get; set; } = string.Empty;

    /// <summary>
    /// Interval between beacon transmissions in hours. Minimum value is 1.
    /// Only used when <see cref="BeaconEnabled"/> is true. Default is 1.
    /// </summary>
    public int BeaconIntervalHours { get; set; } = 1;

    /// <summary>
    /// When true, telemetry data is periodically read from <see cref="TelemetryFilePath"/>
    /// and sent as a text message to <see cref="TelemetryGroup"/>.
    /// </summary>
    public bool TelemetryEnabled { get; set; } = false;

    /// <summary>Full path to the JSON file that provides the telemetry values.</summary>
    public string TelemetryFilePath { get; set; } = string.Empty;

    /// <summary>Target group for telemetry messages (e.g. "#262").</summary>
    public string TelemetryGroup { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of hours (0–23) at which telemetry is sent each day.
    /// Example: "11,15" sends at 11:00 and 15:00. Empty string disables scheduled sending.
    /// </summary>
    public string TelemetryScheduleHours { get; set; } = "11";

    /// <summary>
    /// Mapping of JSON file keys to display label and unit for the telemetry message.
    /// Up to 5 entries are recommended to keep the message within the MeshCom 150-char limit.
    /// </summary>
    public List<TelemetryMappingEntry> TelemetryMapping { get; set; } = [];

    /// <summary>
    /// When true, the POST /api/telemetry endpoint accepts telemetry JSON from external
    /// sources (e.g. Home Assistant) and writes it to <see cref="TelemetryFilePath"/>.
    /// </summary>
    public bool TelemetryApiEnabled { get; set; } = false;

    /// <summary>
    /// Optional API key for the POST /api/telemetry endpoint.
    /// When non-empty the caller must supply a matching X-Api-Key request header.
    /// Leave empty to disable authentication (only recommended in trusted networks).
    /// </summary>
    public string TelemetryApiKey { get; set; } = string.Empty;

    /// <summary>
    /// UI language. Supported values: "de" (German, default) and "en" (English).
    /// </summary>
    public string Language { get; set; } = "de";

    /// <summary>Optional database sink. Set Provider to "mysql" or "influxdb2" to activate.</summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>Optional webhook: HTTP POST on incoming messages, position beacons and/or telemetry.</summary>
    public WebhookSettings Webhook { get; set; } = new();

    /// <summary>Optional QRZ.com XML API integration for callsign lookups in the MH list.</summary>
    public QrzSettings Qrz { get; set; } = new();
}
