namespace MeshcomWebDesk.Models;

/// <summary>
/// A user-defined gateway dashboard source. The URL must point to the <c>rakgw.html</c>
/// page of a MeshCom dashboard (same HTML layout as the OE / DL / IT dashboards).
/// Callsigns scraped from every enabled source are merged with the built-in preset(s).
/// </summary>
public class GatewaySourceEntry
{
    /// <summary>Short display name (e.g. "IT – dig-italia").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Full URL of the dashboard gateway page, e.g. https://meshcom.dig-italia.it/rakgw.html</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>When false, the source is kept but not scraped.</summary>
    public bool Enabled { get; set; } = true;
}
