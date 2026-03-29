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
    public string MyCallsign { get; set; } = "DH1FR-2";

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
}
