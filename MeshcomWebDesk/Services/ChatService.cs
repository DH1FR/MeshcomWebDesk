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
    private readonly QsoSummaryService _qsoSummary;
    private MqttService?      _mqtt;

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
    /// Raised when an incoming direct message addressed to us starts with "--" (bot command).
    /// Fired after the message is recorded in the tab so the bot reply appears after it.
    /// </summary>
    public event Action<MeshcomMessage>? OnBotCommand;

    /// <summary>
    /// Raised when an incoming packet's sender matches a <see cref="MeshcomSettings.WatchCallsigns"/> entry.
    /// Arguments: received callsign (as-is), the triggering message.
    /// </summary>
    public event Action<string, MeshcomMessage>? OnWatchlistHit;

    /// <summary>
    /// The key of the last tab the user actively selected.
    /// Persisted in memory (singleton lifetime) so Chat.razor can restore it
    /// immediately in OnInitialized without requiring JS interop.
    /// </summary>
    public string ActiveTabKey { get; set; } = string.Empty;

    public ChatService(IOptionsMonitor<MeshcomSettings> settings, ILogger<ChatService> logger, IMonitorDataSink sink, WebhookService webhook, QsoSummaryService qsoSummary)
    {
        _settings   = settings.CurrentValue;
        _logger     = logger;
        _sink       = sink;
        _webhook    = webhook;
        _qsoSummary = qsoSummary;
        settings.OnChange(s => _settings = s);
    }

    /// <summary>Injects MqttService after construction to break the circular dependency.</summary>
    public void SetMqttService(MqttService mqtt) => _mqtt = mqtt;

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
            // Only route to the sender's DM tab when the message is actually addressed to us.
            // MeshCom propagates direct messages as broadcasts over the mesh, but message.To
            // still contains the intended recipient. Messages from A to C must NOT appear in
            // the DM tab with A – they belong in the broadcast ("*") tab.
            bool addressedToUs = string.Equals(message.To, _settings.MyCallsign, StringComparison.OrdinalIgnoreCase);
            tabKey = addressedToUs && !string.IsNullOrEmpty(message.From) && _tabs.ContainsKey(message.From)
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

        // Update MH list BEFORE triggering the auto-reply so that RSSI, relay path and
        // hardware data from this message are available to ExpandVariables immediately.
        UpdateMhList(message);

        ChatTab? tab = null;
        bool wasNewDirect = false;
        if (tabAllowed)
            tab = GetOrCreateTab(tabKey, out wasNewDirect);
        lock (_lock)
        {
            AppendToMonitor(message);
            if (tab != null)
            {
                tab.Messages.Add(message);
                tab.UnreadCount++;
            }
        }

        // Fire OnNewDirectTab AFTER the incoming message is in the tab so the auto-reply
        // (AddOutgoingMessage) appears after it in the conversation, not before.
        if (wasNewDirect)
            OnNewDirectTab?.Invoke(message.From);

        // Check QSO summary for new direct tabs created by incoming messages
        if (wasNewDirect && tab != null)
            _ = CheckQsoSummaryAsync(tab, tabKey);

        NotifyChange();
        _ = _webhook.SendAsync(message, "message");
        _ = _mqtt?.PublishAsync(message, "message");
        CheckWatchlist(message);

        // Fire bot command event for direct messages
        if (!message.IsBroadcast &&
            string.Equals(message.To, _settings.MyCallsign, StringComparison.OrdinalIgnoreCase) &&
            MeshcomWebDesk.Services.Bot.BotCommandService.IsCommand(message.Text))
            OnBotCommand?.Invoke(message);
    }

    /// <summary>
    /// Add an outgoing message to the correct tab.
    /// </summary>
    public void AddOutgoingMessage(MeshcomMessage message)
    {
        // Determine tab key: for broadcast ("*") use the "*" tab, otherwise use message.To directly.
        // "#alle" is stored in message.To as-is (the tab key) – do NOT remap it to "*".
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
        ActiveTabKey = key;
        NotifyChange();

        // Async fire-and-forget: check if a QSO summary exists for this direct tab
        if (key != "*" && !key.StartsWith('#'))
            _ = CheckQsoSummaryAsync(tab, key);

        return tab;
    }

    /// <summary>
    /// Public entry point so the UI can trigger a QSO summary check
    /// when a tab is selected that has no icon yet.
    /// Guards against duplicate concurrent checks via <see cref="ChatTab.QsoSummaryCheckPending"/>.
    /// </summary>
    public void TriggerQsoSummaryCheck(ChatTab tab, string callsign)
    {
        if (tab.QsoSummaryCheckPending) return;
        tab.QsoSummaryCheckPending = true;
        _ = CheckQsoSummaryAsync(tab, callsign);
    }

    /// <summary>Checks whether a QSO summary exists and sets the flag on the tab.</summary>
    private async Task CheckQsoSummaryAsync(ChatTab tab, string callsign)
    {
        try
        {
            var callsignBase = callsign.Contains('-') ? callsign[..callsign.IndexOf('-')] : callsign;
            tab.QsoSummaryCallsignBase = callsignBase;
            _logger.LogInformation("ChatService: CheckQsoSummaryAsync tab={Tab} callsignBase={Base}", callsign, callsignBase);
            tab.HasQsoSummary = await _qsoSummary.HasSummaryAsync(callsignBase);
            _logger.LogInformation("ChatService: CheckQsoSummaryAsync tab={Tab} → HasQsoSummary={Result}", callsign, tab.HasQsoSummary);
            if (tab.HasQsoSummary)
                NotifyChange();
        }
        finally
        {
            tab.QsoSummaryCheckPending = false;
        }
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
    /// <para>
    /// If no message with that exact sequence number is found (because the node never
    /// echoed back a <c>{NNN}</c> marker), falls back to matching the <em>oldest</em>
    /// unacknowledged outgoing message addressed to <paramref name="ackSender"/>.
    /// Uses FirstOrDefault (oldest first) so rapid multi-message sequences are matched
    /// in the correct order.
    /// </para>
    /// </summary>
    public void MarkMessageAcknowledged(string sequenceNumber, string? ackSender = null, bool isGateway = false)
    {
        lock (_lock)
        {
            // Primary match: exact sequence number
            var msg = _allMessages.FirstOrDefault(m =>
                m.IsOutgoing && m.SequenceNumber == sequenceNumber);

            // Fallback: match oldest unacknowledged outgoing message to the ACK sender.
            // This covers the case where the node never sent a {NNN} echo so the
            // outgoing message still has SequenceNumber = "TX".
            if (msg == null && ackSender != null)
            {
                msg = _allMessages.FirstOrDefault(m =>
                    m.IsOutgoing &&
                    !m.IsAcknowledged &&
                    string.Equals(m.To, ackSender, StringComparison.OrdinalIgnoreCase));
            }

            if (msg != null)
            {
                msg.SequenceNumber    = sequenceNumber;   // assign real seq# if available
                msg.IsAcknowledged    = true;
                msg.IsGatewayDelivered = isGateway;
            }
        }
        NotifyChange();
    }

    /// <summary>
    /// Processes an incoming APRS ACK: marks the matched outgoing message as delivered,
    /// updates the relay path and signal data for the sending station in the MH list
    /// (so the connection appears on the map), and appends the ACK to the monitor feed.
    /// </summary>
    public void AddAck(MeshcomMessage message)
    {
        if (message.SequenceNumber != null)
        {
            // src_type "udp" means the ACK arrived via the Internet Gateway, not direct LoRa
            var isGateway = string.Equals(message.SrcType, "udp", StringComparison.OrdinalIgnoreCase);
            MarkMessageAcknowledged(message.SequenceNumber, message.From, isGateway);
        }

        // Update relay path / RSSI for this station so the map shows the connection
        UpdateMhList(message);

        lock (_lock) { AppendToMonitor(message); }
        NotifyChange();
        CheckWatchlist(message);
    }

    /// <summary>Remove all entries from the MH list.
    public void ClearMhList()
    {
        _mhList.Clear();
        NotifyChange();
    }

    public void RemoveFromMhList(string callsign)
    {
        _mhList.TryRemove(callsign, out _);
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
            {
                bool isGroup = tab.Key.StartsWith('#');
                bool tabAllowed = !isGroup
                    || !_settings.GroupFilterEnabled
                    || _settings.Groups.Contains(tab.Key, StringComparer.OrdinalIgnoreCase);
                if (tabAllowed)
                    _tabs[tab.Key] = tab;
            }

            _mhList.Clear();
            foreach (var station in snapshot.MhList)
                _mhList[station.Callsign] = station;
        }
        NotifyChange();

        // Async: check QSO summary for all restored direct tabs
        foreach (var tab in _tabs.Values.Where(t => t.Key != "*" && !t.Key.StartsWith('#')))
            _ = CheckQsoSummaryAsync(tab, tab.Key);
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
        _ = _mqtt?.PublishAsync(message, "position");
        CheckWatchlist(message);
    }

    /// <summary>
    /// Process a telemetry packet
    /// Does NOT open or update any chat tab.
    /// </summary>
    public void AddTelemetry(MeshcomMessage message)
    {
        UpdateMhList(message);
        lock (_lock) { AppendToMonitor(message); }
        NotifyChange();
        _ = _webhook.SendAsync(message, "telemetry");
        _ = _mqtt?.PublishAsync(message, "telemetry");
        CheckWatchlist(message);
    }

    /// <summary>Get a specific tab.
    public ChatTab? GetTab(string key)
    {
        _tabs.TryGetValue(key, out var tab);
        return tab;
    }

    /// <summary>
    /// Returns the group label entry for a group number or tab key.
    /// Accepts both "#262" (tab key) and "262" (raw wire value) formats.
    /// Returns null if no label is configured for that group.
    /// </summary>
    public GroupLabelEntry? GetGroupLabel(string tabKey)
    {
        var number = tabKey.TrimStart('#');
        if (string.IsNullOrEmpty(number)) return null;
        return _settings.GroupLabels.FirstOrDefault(g =>
            string.Equals(g.Group, number, StringComparison.OrdinalIgnoreCase));
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
                MessageCount     = (message.IsPositionBeacon || message.IsTelemetry || message.IsAck) ? 0 : 1,
                LastDestination  = message.IsAck ? string.Empty : message.To,
                LastMessage      = message.IsAck ? string.Empty : message.Text,
                LastRssi         = message.Rssi,
                LastSnr          = message.Snr,
                Latitude         = message.Latitude,
                Longitude        = message.Longitude,
                Altitude         = message.Altitude,
                LastPositionTime = message.Latitude.HasValue ? message.Timestamp : null,
                Battery          = message.Battery,
                HwId             = message.HwId,
                Firmware         = message.Firmware,
                LastRelayPath        = message.RelayPath,
                HopCount             = message.RelayPath?.Split(',').Length - 1 ?? 0,
                RelayPathCount       = message.RelayPath != null ? 1 : 0,
                LastSrcType          = message.SrcType,
                DirectLinkConfirmed  = (message.IsAck || (!message.IsPositionBeacon && !message.IsTelemetry))
                                       && message.RelayPath == null,
                Temp1             = message.IsTelemetry ? message.Temp1     : null,
                Humidity          = message.IsTelemetry ? message.Humidity  : null,
                Pressure          = message.IsTelemetry ? message.Pressure  : null,
                LastTelemetryTime = message.IsTelemetry ? message.Timestamp : null,
            },
            (_, s) =>
            {
                s.LastHeard = message.Timestamp;
                if (!message.IsPositionBeacon && !message.IsTelemetry && !message.IsAck)
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
                if (!string.IsNullOrEmpty(message.SrcType))  s.LastSrcType = message.SrcType;
                if ((message.IsAck || (!message.IsPositionBeacon && !message.IsTelemetry))
                    && message.RelayPath == null)
                    s.DirectLinkConfirmed = true;

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

    private ChatTab GetOrCreateTab(string key, out bool wasNewDirect)
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

        var tab = _tabs.GetOrAdd(key, newTab);
        wasNewDirect = ReferenceEquals(tab, newTab) && key != "*" && !key.StartsWith('#');

        if (wasNewDirect)
            OnNewTab?.Invoke(key);

        return tab;
    }

    // Convenience overload for callers that don't need the wasNewDirect flag.
    private ChatTab GetOrCreateTab(string key) => GetOrCreateTab(key, out _);

    /// <summary>
    /// Checks <paramref name="message"/>.From against every entry in <see cref="MeshcomSettings.WatchCallsigns"/>.
    /// Fires <see cref="OnWatchlistHit"/> on the first match.
    /// </summary>
    private void CheckWatchlist(MeshcomMessage message)
    {
        if (string.IsNullOrEmpty(message.From)) return;
        var list = _settings.WatchCallsigns;
        if (list.Count == 0) return;

        var typeLabel = message.IsAck ? "ACK" : message.IsPositionBeacon ? "POS" : message.IsTelemetry ? "TEL" : "MSG";
        _logger.LogDebug("Watchlist check: From={From} Type={Type} List=[{List}]",
            message.From, typeLabel, string.Join(",", list));

        if (message.IsAck            && !_settings.WatchOnAck)      { _logger.LogDebug("Watchlist: ACK filtered out"); return; }
        if (message.IsPositionBeacon && !_settings.WatchOnPosition)  { _logger.LogDebug("Watchlist: POS filtered out"); return; }
        if (message.IsTelemetry      && !_settings.WatchOnTelemetry) { _logger.LogDebug("Watchlist: TEL filtered out"); return; }
        if (!message.IsAck && !message.IsPositionBeacon && !message.IsTelemetry && !_settings.WatchOnMessage) { _logger.LogDebug("Watchlist: MSG filtered out"); return; }

        foreach (var entry in list)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var matched = MatchesWatchEntry(message.From, entry.Trim());
            _logger.LogDebug("Watchlist: '{From}' vs '{Entry}' → {Match}", message.From, entry.Trim(), matched);
            if (matched)
            {
                _logger.LogInformation("Watchlist HIT: {From} ({Type})", message.From, typeLabel);
                OnWatchlistHit?.Invoke(message.From, message);
                return;
            }
        }
    }

    /// <summary>
    /// Returns true when <paramref name="callsign"/> matches a watchlist <paramref name="entry"/>.
    /// Entry with SSID (contains '-') → exact match. Entry without SSID → base-callsign match.
    /// </summary>
    private static bool MatchesWatchEntry(string callsign, string entry)
    {
        if (entry.Contains('-'))
            return string.Equals(callsign, entry, StringComparison.OrdinalIgnoreCase);
        var baseCs = callsign.Contains('-') ? callsign[..callsign.IndexOf('-')] : callsign;
        return string.Equals(baseCs, entry, StringComparison.OrdinalIgnoreCase);
    }

    private void NotifyChange()
    {
        OnChange?.Invoke();
    }
}
