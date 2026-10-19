namespace MeshcomWebDesk.Models;

public class MqttSettings
{
    public bool   Enabled       { get; set; } = false;
    public string Host          { get; set; } = "localhost";
    public int    Port          { get; set; } = 1883;
    public string ClientId      { get; set; } = "meshcom-webdesk-" + Guid.NewGuid().ToString("N")[..6];
    public string Username      { get; set; } = string.Empty;

    /// <summary>Stored encrypted with "dp:" prefix via ASP.NET Core Data Protection.</summary>
    public string Password      { get; set; } = string.Empty;

    public bool   UseTls        { get; set; } = false;

    /// <summary>Topic prefix for all published and subscribed topics (e.g. "meshcom").</summary>
    public string TopicPrefix   { get; set; } = "meshcom";

    // --- Publisher ---
    public bool PublishMessage   { get; set; } = true;
    public bool PublishPosition  { get; set; } = false;
    public bool PublishTelemetry { get; set; } = false;

    // --- Unread alert ---
    /// <summary>
    /// When true, a direct message that nobody has actually looked at within
    /// <see cref="UnreadAlertSeconds"/> is published to <c>{prefix}/unread/{callsign}</c>
    /// (and <c>{prefix}/read/{callsign}</c> once it has been seen).
    /// </summary>
    public bool UnreadAlertEnabled { get; set; } = true;

    /// <summary>Seconds a direct message may stay unseen before the unread alert is published.</summary>
    public int UnreadAlertSeconds { get; set; } = 60;

    /// <summary>
    /// A viewer only counts as "looking" while its browser tab is visible and the last
    /// mouse/keyboard/touch activity is younger than this many seconds.
    /// </summary>
    public int UnreadActiveSeconds { get; set; } = 30;

    // --- Subscriber ---
    /// <summary>When true, subscribes to send-topics and forwards them as outgoing UDP messages.</summary>
    public bool SubscribeEnabled { get; set; } = false;

    /// <summary>When true, every MQTT publish and received send-command is logged at Information level.</summary>
    public bool LogRequests { get; set; } = false;
}
