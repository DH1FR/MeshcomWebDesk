namespace MeshcomWebClient.Models;

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
    /// When true, an automatic reply is sent once when a brand-new direct chat tab is
    /// opened by an incoming message (i.e. first contact from a callsign).
    /// Default is false.
    /// </summary>
    public bool AutoReplyEnabled { get; set; } = false;

    /// <summary>
    /// Text sent as the automatic reply. Only used when <see cref="AutoReplyEnabled"/> is true.
    /// </summary>
    public string AutoReplyText { get; set; } =
        "---=== MeshcomWebClient - https://github.com/DH1FR/MeshcomWebClient ===---";
}
