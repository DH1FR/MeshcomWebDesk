namespace MeshcomWebDesk.Models;

/// <summary>
/// WebDesk-as-KISS-hub: WebDesk opens its own KISS/TCP listener and fans the traffic of one
/// KISS node out to several downstream apps (Direwolf, YAAC, APRSdroid …), so the node's single
/// client slot is not blocked. See <c>docs/kiss-mode-analysis.md</c> §5.7.
/// </summary>
public class KissHubSettings
{
    /// <summary>Master switch for the hub listener.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>TCP port WebDesk listens on for downstream KISS clients.</summary>
    public int Port { get; set; } = 8001;

    /// <summary>
    /// Id of the KISS node whose traffic the hub serves. <c>null</c> / empty = the primary node.
    /// The node must have <see cref="NodeProfile.Transport"/> = <see cref="NodeTransport.Kiss"/>.
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>
    /// When true the listener binds <c>0.0.0.0</c> (reachable from the LAN, e.g. a phone running
    /// APRSdroid). When false it binds <c>127.0.0.1</c> only (same machine). No authentication
    /// either way – keep it on a trusted network.
    /// </summary>
    public bool BindLan { get; set; } = false;
}
