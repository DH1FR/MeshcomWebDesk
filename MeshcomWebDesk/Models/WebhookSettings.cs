namespace MeshcomWebDesk.Models;

public class WebhookSettings
{
    /// <summary>When true, an HTTP POST is sent to <see cref="Url"/> on matching events.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Target URL. Must accept HTTP POST with a JSON body.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Fire on incoming chat messages (msg).</summary>
    public bool OnMessage { get; set; } = true;

    /// <summary>Fire on incoming position beacons (pos).</summary>
    public bool OnPosition { get; set; } = false;

    /// <summary>Fire on incoming telemetry packets (tele).</summary>
    public bool OnTelemetry { get; set; } = false;
}
