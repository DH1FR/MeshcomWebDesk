namespace MeshcomWebDesk.Models;

public class ChatTab
{
    /// <summary>
    /// Id des NodeProfile, zu dem dieser Tab gehört. Null = Legacy-Modus (kein Multi-Node).
    /// </summary>
    public Guid? NodeId { get; set; }

    /// <summary>
    /// Unique key for this tab. Callsign, group name, or special values like "*" (broadcast).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display title for the tab.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Messages belonging to this conversation.</summary>
    public List<MeshcomMessage> Messages { get; set; } = [];

    /// <summary>
    /// Thread-safe message count. Updated by ChatService under its internal lock
    /// whenever a message is added or removed, so Razor templates can read this
    /// without holding the lock and without touching <see cref="Messages"/> directly.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int MessageCount { get; set; }

    /// <summary>Number of messages received while this tab was not active. Not persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UnreadCount { get; set; }

    /// <summary>
    /// When true, a QSO summary is available for the remote callsign (direct tabs only). Not persisted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasQsoSummary { get; set; }

    /// <summary>Base callsign (without SSID) used for the QSO summary lookup. Not persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? QsoSummaryCallsignBase { get; set; }

    /// <summary>True while the async QSO summary check is running. Prevents duplicate DB queries.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool QsoSummaryCheckPending { get; set; }
}
