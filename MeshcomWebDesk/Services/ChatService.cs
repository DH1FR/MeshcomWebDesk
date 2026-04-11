using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.Database;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Manages chat tabs and routes messages to the correct conversation.
/// Thread-safe singleton shared across all Blazor circuits.
/// </summary>
public class ChatService
{
    private readonly ConcurrentDictionary<string, ChatTab> _tabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HeardStation> _mhList = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MeshcomMessage> _allMessages = [];
    private readonly object _lock = new();
    private MeshcomSettings _settings;
    private readonly ILogger<ChatService> _logger;
    private readonly IMonitorDataSink _sink;
    private readonly WebhookService   _webhook;

    /// <summary>
    /// Rolling deduplication cache.
    /// Key = "seq:{From}:{SeqNr}"  (primary, when SequenceNumber is present)
    ///       "txt:{From}:{To}:{Text}" (fallback, when no sequence number).
    /// Value = time of first receipt. Entries older than <see cref="DedupWindow"/> are pruned on each check.
    /// </summary>
    private readonly Dictionary<string, DateTime> _seenMessageKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(10);

    /// <summary>Raised when a message is added or a tab changes.</summary>
    public event Action? OnChange;

    /// <summary>
    /// Raised when a brand-new direct (1:1) tab is created by an incoming message.
    /// The argument is the remote callsign. Not raised for broadcast (*) or group (#) tabs,
    /// and not raised when tabs are restored from a snapshot or opened manually.
    /// </summary>
    public event Action<string>? OnNewDirectTab;

    /// <summary>
    /// Raised whenever a brand-new direct (1:1) tab is created, both by incoming messages
    /// and by manual tab opening. Not raised for broadcast (*) or group (#) tabs.
    /// </summary>
    public event Action<string>? OnNewTab;

    /// <summary>
    /// The key of the last tab the user actively selected.
    /// Persisted in memory (singleton lifetime) so Chat.razor can restore it
    /// immediately in OnInitialized without requiring JS interop.
    /// </summary>
    public string ActiveTabKey { get; set; } = string.Empty;

    public ChatService(IOptionsMonitor<MeshcomSettings> settings, ILogger<ChatService> logger, IMonitorDataSink sink, WebhookService webhook)
    {
        _settings = settings.CurrentValue;
        _logger   = logger;
        _sink     = sink;
        _webhook  = webhook;
        settings.OnChange(s => _settings = s);
    }

    /// <summary>All open tabs.</summary>
    public IReadOnlyList<ChatTab> Tabs
    {
        get
        {
            lock (_lock)
            {
                return _tabs.Values.ToList();
            }
        }
    }

    /// <summary>All messages sorted newest-first (for the bottom pane).</summary>
    public IReadOnlyList<MeshcomMessage> AllMessages
    {
        get
        {
            lock (_lock)
            {
                return _allMessages.OrderByDescending(m => m.Timestamp).ToList();
            }
        }
    }

    /// <summary>Most recently heard stations, sorted by last heard descending.</summary>
    public IReadOnlyList<HeardStation> MhList =>
        _mhList.Values.OrderByDescending(s => s.LastHeard).ToList();

    /// <summary>
    /// Route an incoming message to the correct tab. Creates tab automatically if needed.
    /// Duplicate packets (same sender + sequence number within <see cref="DedupWindow"/>) are silently dropped.
    /// </summary>
    public void AddIncomingMessage(MeshcomMessage message)
    {
        // Deduplication: Meshcom 4.0 may deliver the same packet multiple times via different
        // mesh routes. Use the sender-assigned sequence number as the primary key.
        if (IsDuplicate(message))
        {
            _logger.LogDebug("Duplicate message suppressed: From={From} Seq={Seq} Text={Text}",
                message.From, message.SequenceNumber, message.Text);
            return;
        }

        // Determine tab key based on destination:
        //   Broadcast from known correspondent     → sender's direct tab
        //   Broadcast from unknown station         → tab "*" ("Alle")
        //   Direct to us (MyCallsign)              → tab by sender callsign
        //   Group (any other dst)                  → tab "#<group>"
        string tabKey;
        if (message.IsBroadcast)
        {
            // If an open direct tab with this sender already exists, prefer it.
            // Handles the MeshCom case where a station replies via broadcast.
            tabKey = !string.IsNullOrEmpty(message.From) && _tabs.ContainsKey(message.From)
                ? message.From
                : "*";
        }
        else if (string.Equals(message.To, _settings.MyCallsign, StringComparison.OrdinalIgnoreCase))
        {
            tabKey = message.From;
        }
        else
        {
            tabKey = "#" + message.To;
        }

        // For group messages, only auto-create a tab if the filter is disabled or the group is whitelisted.
        // Manually opened tabs (via OpenTab) are not affected by this restriction.
        bool isGroup = tabKey.StartsWith('#');
        bool tabAllowed = !isGroup
            || !_settings.GroupFilterEnabled
            || _settings.Groups.Contains(tabKey, StringComparer.OrdinalIgnoreCase);

        ChatTab? tab = tabAllowed ? GetOrCreateTab(tabKey, triggerAutoReply: true) : null;
        lock (_lock)
        {
            AppendToMonitor(message);
            if (tab != null)
            {
                tab.Messages.Add(message);
                tab.UnreadCount++;
            }
        }

        UpdateMhList(message);
        NotifyChange();
        _ = _webhook.SendAsync(message, "message");
    }

    /// <summary>
    /// Add an outgoing message to the correct tab.
    /// </summary>
    public void AddOutgoingMessage(MeshcomMessage message)
    {
        // Determine tab key: for broadcast use "*", otherwise use the destination callsign
        var tabKey = message.IsBroadcast ? "*" : message.To;
        var tab = GetOrCreateTab(tabKey);
        lock (_lock)
        {
            AppendToMonitor(message);
            tab.Messages.Add(message);
        }

        NotifyChange();
    }

    /// <summary>
    /// Add a message to the raw feed only, without routing it to any tab.
    /// Used for unparseable device data (status, telemetry, etc.).
    /// </summary>
    public void AddRawMessage(MeshcomMessage message)
    {
        lock (_lock)
        {
            AppendToMonitor(message);
        }

        NotifyChange();
    }

    /// <summary>Open a new tab manually.</summary>
    public ChatTab OpenTab(string key)
    {
        var tab = GetOrCreateTab(key);
        NotifyChange();
        return tab;
    }

    /// <summary>Close a tab.</summary>
    public void CloseTab(string key)
    {
        _tabs.TryRemove(key, out _);
        NotifyChange();
    }

    /// <summary>Resets the unread counter for the given tab (call when user switches to it).</summary>
    public void ClearUnread(string key)
    {
        if (_tabs.TryGetValue(key, out var tab))
            lock (_lock) { tab.UnreadCount = 0; }
    }

    /// <summary>
    /// Assigns the node sequence number (from the echo packet) to the most recent
    /// outgoing message sent to <paramref name="destination"/> that has no sequence yet.
    /// </summary>
    public void AssignOutgoingSequence(string destination, string sequenceNumber)
    {
        lock (_lock)
        {
            // m.To may carry the '#' prefix (e.g. "#9") while the node echo uses the raw
            // group number (e.g. "9") – strip '#' on both sides before comparing.
            var msg = _allMessages.LastOrDefault(m =>
                m.IsOutgoing &&
                m.SequenceNumber == null &&
                string.Equals(m.To.TrimStart('#'), destination.TrimStart('#'), StringComparison.OrdinalIgnoreCase));
            if (msg != null)
                msg.SequenceNumber = sequenceNumber;
        }
        NotifyChange();
    }

    /// <summary>
    /// Marks the outgoing message with the given sequence number as acknowledged
    /// after an APRS ACK packet has been received.
    /// </summary>
    public void MarkMessageAcknowledged(string sequenceNumber)
    {
        lock (_lock)
        {
            var msg = _allMessages.LastOrDefault(m =>
                m.IsOutgoing && m.SequenceNumber == sequenceNumber);
            if (msg != null)
                msg.IsAcknowledged = true;
        }
        NotifyChange();
    }

    /// <summary>Remove all entries from the MH list.</summary>
    public void ClearMhList()
    {
        _mhList.Clear();
        NotifyChange();
    }

    /// <summary>
    /// Clears all chat tabs, MH list and monitor entries.
    /// Called from the UI "Daten löschen" page.
    /// </summary>
    public void ClearAllData()
    {
        lock (_lock)
        {
            _tabs.Clear();
            _mhList.Clear();
            _allMessages.Clear();
            _seenMessageKeys.Clear();
        }
        NotifyChange();
    }

    /// <summary>Creates a thread-safe snapshot of the current state for persistence.</summary>
    public PersistenceSnapshot CreateSnapshot()
    {
        lock (_lock)
        {
            return new PersistenceSnapshot
            {
                SavedAt = DateTime.Now,
                Tabs = _tabs.Values
                    .Select(t => new ChatTab
                    {
                        Key      = t.Key,
                        Title    = t.Title,
                        Messages = t.Messages.ToList()
                    })
                    .ToList(),
                MhList          = _mhList.Values.ToList(),
                MonitorMessages = _allMessages.ToList()
            };
        }
    }

    /// <summary>Restores state from a previously saved snapshot.</summary>
    public void LoadSnapshot(PersistenceSnapshot snapshot)
    {
        lock (_lock)
        {
            _allMessages.Clear();
            _allMessages.AddRange(snapshot.MonitorMessages.TakeLast(_settings.MonitorMaxMessages));

            _tabs.Clear();
            foreach (var tab in snapshot.Tabs)
                _tabs[tab.Key] = tab;

            _mhList.Clear();
            foreach (var station in snapshot.MhList)
                _mhList[station.Callsign] = station;
        }
        NotifyChange();
    }

    /// <summary>
    /// Process a pure position beacon: update MH position data and add to raw feed.
    /// Does NOT open or update any chat tab.
    /// </summary>
    public void AddPositionBeacon(MeshcomMessage message)
    {
        UpdateMhList(message);
        lock (_lock) { AppendToMonitor(message); }
        NotifyChange();
        _ = _webhook.SendAsync(message, "position");
    }

    /// <summary>
    /// Process a telemetry packet: update MH data and add to monitor feed.
    /// Does NOT open or update any chat tab.
    /// </summary>
    public void AddTelemetry(MeshcomMessage message)
    {
        UpdateMhList(message);
        lock (_lock) { AppendToMonitor(message); }
        NotifyChange();
        _ = _webhook.SendAsync(message, "telemetry");
    }

    /// <summary>Get a specific tab.</summary>
    public ChatTab? GetTab(string key)
    {
        _tabs.TryGetValue(key, out var tab);
        return tab;
    }

    /// <summary>Get a thread-safe snapshot of a tab's messages.</summary>
    public IReadOnlyList<MeshcomMessage> GetTabMessages(string key)
    {
        if (!_tabs.TryGetValue(key, out var tab))
            return [];

        lock (_lock)
        {
            return tab.Messages.ToList();
        }
    }

    /// <summary>
    /// Returns true when an identical message was already processed within <see cref="DedupWindow"/>.
    /// Registers the message as seen on first encounter.
    /// Priority: msg_id (most reliable) → seq:{From}:{SeqNr} → txt:{From}:{To}:{Text}
    /// </summary>
    private bool IsDuplicate(MeshcomMessage message)
    {
        string key = !string.IsNullOrEmpty(message.MsgId)
            ? $"mid:{message.MsgId}"
            : !string.IsNullOrEmpty(message.SequenceNumber)
                ? $"seq:{message.From}:{message.SequenceNumber}"
                : $"txt:{message.From}:{message.To}:{message.Text}";

        lock (_lock)
        {
            var now    = DateTime.Now;
            var cutoff = now - DedupWindow;

            // Prune expired entries to keep the dictionary from growing unbounded
            var expired = _seenMessageKeys
                .Where(kv => kv.Value < cutoff)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var k in expired)
                _seenMessageKeys.Remove(k);

            if (_seenMessageKeys.ContainsKey(key))
                return true;

            _seenMessageKeys[key] = now;
            return false;
        }
    }

    private void UpdateMhList(MeshcomMessage message)
    {
        if (string.IsNullOrEmpty(message.From))
            return;

        _mhList.AddOrUpdate(
            message.From,
            _ => new HeardStation
            {
                Callsign         = message.From,
                FirstHeard       = message.Timestamp,
                LastHeard        = message.Timestamp,
                MessageCount     = (message.IsPositionBeacon || message.IsTelemetry) ? 0 : 1,
                LastDestination  = message.To,
                LastMessage      = message.Text,
                LastRssi         = message.Rssi,
                LastSnr          = message.Snr,
                Latitude         = message.Latitude,
                Longitude        = message.Longitude,
                Altitude         = message.Altitude,
                LastPositionTime = message.Latitude.HasValue ? message.Timestamp : null,
                Battery          = message.Battery,
                HwId             = message.HwId,
                Firmware         = message.Firmware,
                LastRelayPath    = message.RelayPath,
                HopCount         = message.RelayPath?.Split(',').Length - 1 ?? 0,
                RelayPathCount   = message.RelayPath != null ? 1 : 0,
                Temp1             = message.IsTelemetry ? message.Temp1     : null,
                Humidity          = message.IsTelemetry ? message.Humidity  : null,
                Pressure          = message.IsTelemetry ? message.Pressure  : null,
                LastTelemetryTime = message.IsTelemetry ? message.Timestamp : null,
            },
            (_, s) =>
            {
                s.LastHeard = message.Timestamp;
                if (!message.IsPositionBeacon && !message.IsTelemetry)
                {
                    s.MessageCount++;
                    s.LastDestination = message.To;
                    s.LastMessage     = message.Text;
                }
                if (message.Rssi.HasValue)    s.LastRssi = message.Rssi;
                if (message.Snr.HasValue)     s.LastSnr  = message.Snr;
                if (message.Battery.HasValue) s.Battery  = message.Battery;
                if (message.HwId.HasValue)    s.HwId     = message.HwId;
                if (!string.IsNullOrEmpty(message.Firmware)) s.Firmware = message.Firmware;
                if (message.RelayPath is not null)
                {
                    var hops = message.RelayPath.Split(',').Length - 1;
                    s.HopCount = hops;
                    // Keep count when same path, reset when path changes
                    if (s.LastRelayPath == message.RelayPath)
                        s.RelayPathCount++;
                    else
                        s.RelayPathCount = 1;
                    s.LastRelayPath = message.RelayPath;
                }
                if (message.Latitude.HasValue)
                {
                    s.Latitude         = message.Latitude;
                    s.Longitude        = message.Longitude;
                    s.Altitude         = message.Altitude;
                    s.LastPositionTime = message.Timestamp;
                }
                if (message.IsTelemetry)
                {
                    if (message.Temp1.HasValue)    s.Temp1    = message.Temp1;
                    if (message.Humidity.HasValue)  s.Humidity = message.Humidity;
                    if (message.Pressure.HasValue)  s.Pressure = message.Pressure;
                    s.LastTelemetryTime = message.Timestamp;
                }
                return s;
            });
    }

    /// <summary>
    /// Appends a message to the monitor feed and trims to <see cref="MonitorMaxMessages"/>.
    /// Must be called while holding <see cref="_lock"/>.
    /// </summary>
    private void AppendToMonitor(MeshcomMessage message)
    {
        _allMessages.Add(message);
        if (_allMessages.Count > _settings.MonitorMaxMessages)
            _allMessages.RemoveRange(0, _allMessages.Count - _settings.MonitorMaxMessages);
        _ = _sink.WriteAsync(message);
    }

    private ChatTab GetOrCreateTab(string key, bool triggerAutoReply = false)
    {
        var newTab = new ChatTab
        {
            Key   = key,
            Title = key switch
            {
                "*"              => "Alle",
                _ when key.StartsWith('#') => key,
                _                => key
            }
        };

        var tab    = _tabs.GetOrAdd(key, newTab);
        bool wasNew = ReferenceEquals(tab, newTab);

        if (wasNew && key != "*" && !key.StartsWith('#'))
        {
            OnNewTab?.Invoke(key);
            if (triggerAutoReply)
                OnNewDirectTab?.Invoke(key);
        }

        return tab;
    }

    private void NotifyChange()
    {
        OnChange?.Invoke();
    }
}
