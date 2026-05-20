using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
    // ── Per-node state ────────────────────────────────────────────────────
    /// <summary>
    /// Holds all mutable state that is scoped per Node.
    /// Key = NodeProfile.Id, or <see cref="Guid.Empty"/> for legacy single-node mode.
    /// </summary>
    private sealed class NodeState
    {
        public ConcurrentDictionary<string, ChatTab>      Tabs       { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, HeardStation> MhList     { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<MeshcomMessage>                        Messages   { get; } = [];
        public string                                      ActiveTabKey { get; set; } = string.Empty;
        /// <summary>User-defined tab display order (list of tab keys). Empty = natural insertion order.</summary>
        public List<string>                                TabOrder   { get; set; } = [];
    }

    private readonly ConcurrentDictionary<Guid, NodeState> _nodeState = new();

    /// <summary>Returns (or lazily creates) the state bucket for a concrete <paramref name="nodeId"/>.
    /// Passing <c>null</c> uses <see cref="Guid.Empty"/> (legacy single-node fallback).</summary>
    private NodeState GetState(Guid? nodeId) => _nodeState.GetOrAdd(nodeId ?? Guid.Empty, _ => new NodeState());

    /// <summary>Returns the state for the primary node (or Guid.Empty in legacy mode).</summary>
    private NodeState GetPrimaryState()
    {
        var primaryId = _nodeManager?.PrimaryNode?.Id;
        return GetState(primaryId);
    }

    /// <summary>Resolves <paramref name="nodeId"/> to its state bucket:
    /// <c>null</c> → primary node; explicit Guid → that node's bucket.</summary>
    private NodeState ResolveState(Guid? nodeId) =>
        nodeId is null ? GetPrimaryState() : GetState(nodeId);

    /// <summary>True when <paramref name="nodeId"/> refers to the primary (or only) node.</summary>
    private bool IsPrimaryNode(Guid? nodeId)
    {
        if (nodeId is null || nodeId == Guid.Empty) return true;   // legacy single-node mode
        var primaryId = _nodeManager?.PrimaryNode?.Id;
        return primaryId is null || primaryId == nodeId;
    }

    // ── Shortcuts to the legacy/primary state (Guid.Empty) ───────────────
    private ConcurrentDictionary<string, ChatTab>      _tabs    => GetState(Guid.Empty).Tabs;
    private ConcurrentDictionary<string, HeardStation> _mhList  => GetState(Guid.Empty).MhList;
    private List<MeshcomMessage>                        _allMessages => GetState(Guid.Empty).Messages;

    private readonly object _lock = new();
    private MeshcomSettings _settings;
    private readonly ILogger<ChatService> _logger;
    private readonly IMonitorDataSink _sink;
    private readonly WebhookService   _webhook;
    private readonly QsoSummaryService _qsoSummary;
    private MqttService?      _mqtt;
    private NodeManager?      _nodeManager;

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
    /// Raised when a node-echo timeout occurred – an outgoing UDP packet was
    /// not confirmed by the node within the expected time window.
    /// </summary>
    public event Action? OnEchoTimeout;

    /// <summary>Triggers an echo-timeout notification to all subscribers (e.g. Chat UI).</summary>
    public void NotifyEchoTimeout() => OnEchoTimeout?.Invoke();

    /// <summary>
    /// Raised only when the MH list itself changes (station added, removed, position or
    /// telemetry updated). Map and MH-page subscribe to this instead of <see cref="OnChange"/>
    /// to avoid rebuilding on every chat message.
    /// </summary>
    public event Action? OnMhChange;

    /// <summary>
    /// Raised when a brand-new direct (1:1) tab is created by an incoming message.
    /// The argument is the remote callsign. Not raised for broadcast (*) or group (#) tabs,
    /// and not raised when tabs are restored from a snapshot or opened manually.
    /// </summary>
    public event Action<string, MeshcomMessage>? OnNewDirectTab;

    /// <summary>
    /// Raised for every incoming direct message addressed to our own callsign,
    /// regardless of whether the tab already exists. Used for voice announcements.
    /// Arguments: sender callsign, the message.
    /// </summary>
    public event Action<string, MeshcomMessage>? OnDirectMessage;

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
    /// Raised when a group message is detected as a CQ call (own callsign excluded).
    /// Arguments: sender callsign, group number (e.g. "262"), the raw message text.
    /// </summary>
    public event Action<string, string, string>? OnCqHeard;

    /// <summary>UTC timestamp of the last outgoing transmission. Null if no message has been sent yet.</summary>
    public DateTime? LastTxTime { get; private set; }

    /// <summary>
    /// Remaining cooldown in seconds (0 when ready to transmit).
    /// Calculated from <see cref="LastTxTime"/> and <c>TxCooldownSeconds</c> in settings.
    /// </summary>
    public int TxCooldownRemaining =>
        LastTxTime is { } t && _settings.TxCooldownSeconds > 0
            ? Math.Max(0, Math.Max(5, _settings.TxCooldownSeconds) - (int)(DateTime.UtcNow - t).TotalSeconds)
            : 0;

    /// <summary>Records the current UTC time as the last transmission time.</summary>
    public void RecordTx() => LastTxTime = DateTime.UtcNow;

    // Compiled regex: matches messages that contain "CQ" as a standalone word/abbreviation.
    // Examples matched: "CQ de OE6TZD", "cq cq de DF7AX", "IY6GM CQ 144300", "cQ DO7PAW".
    private static readonly Regex CqRegex = new(
        @"(?<![A-Z0-9])CQ(?![A-Z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The key of the last tab the user actively selected.
    /// Persisted in memory (singleton lifetime) so Chat.razor can restore it
    /// immediately in OnInitialized without requiring JS interop.
    /// Use <see cref="GetActiveTabKey"/> / <see cref="SetActiveTabKey"/> for node-scoped access.
    /// </summary>
    public string ActiveTabKey
    {
        get => GetState(Guid.Empty).ActiveTabKey;
        set => GetState(Guid.Empty).ActiveTabKey = value;
    }

    public string GetActiveTabKey(Guid? nodeId) => ResolveState(nodeId).ActiveTabKey;
    public void   SetActiveTabKey(Guid? nodeId, string key) => ResolveState(nodeId).ActiveTabKey = key;

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

    /// <summary>Injects NodeManager after construction to break the circular dependency.</summary>
    public void SetNodeManager(NodeManager nodeManager) => _nodeManager = nodeManager;

    /// <summary>All open tabs (legacy/primary node).</summary>
    public IReadOnlyList<ChatTab> Tabs => GetTabs(null);

    /// <summary>All open tabs for a specific node.</summary>
    public IReadOnlyList<ChatTab> GetTabs(Guid? nodeId)
    {
        lock (_lock)
        {
            return ResolveState(nodeId).Tabs.Values.ToList();
        }
    }

    /// <summary>Returns the persisted tab order for a node. Empty list = no saved order.</summary>
    public IReadOnlyList<string> GetTabOrder(Guid? nodeId)
    {
        lock (_lock) { return ResolveState(nodeId).TabOrder.ToList(); }
    }

    /// <summary>Saves the tab order for a node so it survives the next snapshot cycle.</summary>
    public void SetTabOrder(Guid? nodeId, IEnumerable<string> order)
    {
        lock (_lock) { ResolveState(nodeId).TabOrder = [.. order]; }
    }

    /// <summary>All messages sorted newest-first (legacy/primary node).</summary>
    public IReadOnlyList<MeshcomMessage> AllMessages => GetAllMessages(null);

    /// <summary>All messages sorted newest-first for a specific node.
    /// Passing <c>null</c> resolves to the primary node (same as <see cref="GetPrimaryState"/>).</summary>
    public IReadOnlyList<MeshcomMessage> GetAllMessages(Guid? nodeId)
    {
        lock (_lock)
        {
            return ResolveState(nodeId).Messages.OrderByDescending(m => m.Timestamp).ToList();
        }
    }

    /// <summary>Most recently heard stations (primary node only), sorted by last heard descending.</summary>
    public IReadOnlyList<HeardStation> MhList => GetPrimaryState().MhList.Values.OrderByDescending(s => s.LastHeard).ToList();

    /// <summary>Most recently heard stations for a specific node, sorted by last heard descending.</summary>
    public IReadOnlyList<HeardStation> GetMhList(Guid? nodeId) =>
        ResolveState(nodeId).MhList.Values.OrderByDescending(s => s.LastHeard).ToList();

    /// <summary>
    /// Route an incoming message to the correct tab. Creates tab automatically if needed.
    /// Duplicate packets (same sender + sequence number within <see cref="DedupWindow"/>) are silently dropped.
    /// </summary>
    public void AddIncomingMessage(MeshcomMessage message) => AddIncomingMessage(message, message.NodeId);

    /// <summary>Node-scoped variant: routes the message into the state of <paramref name="nodeId"/>.</summary>
    public void AddIncomingMessage(MeshcomMessage message, Guid? nodeId)
    {
        var state = ResolveState(nodeId);

        // Deduplication: Meshcom 4.0 may deliver the same packet multiple times via different
        // mesh routes. Use the sender-assigned sequence number as the primary key.
        if (IsDuplicate(message))
        {
            _logger.LogDebug("Duplicate message suppressed: From={From} Seq={Seq} Text={Text}",
                message.From, message.SequenceNumber, message.Text);
            return;
        }

        // Resolve the own callsign for this node
        var myCallsign = _nodeManager?.GetCallsignForNode(nodeId) ?? _settings.MyCallsign;

        // Determine tab key based on destination:
        //   Broadcast from known correspondent     → sender's direct tab
        //   Broadcast from unknown station         → tab "*" ("Alle")
        //   Direct to us                           → tab by sender callsign
        //   Group (any other dst)                  → tab "#<group>"
        //
        // "Direct to us" means message.To matches our configured callsign (myCallsign)
        // OR the node is identified AND message.To looks like a callsign addressed to this node.
        //
        // Guard: MeshCom sends group numbers as bare digits (e.g. "26299") without '#'.
        // These must NOT be treated as direct messages. A real callsign always contains letters.
        // Additionally only treat as direct-to-node when To matches this node's hardware callsign
        // (i.e. the callsign the node actually uses on-air, which may differ from NodeProfile.Callsign).
        string tabKey;
        bool looksLikeCallsign = !string.IsNullOrEmpty(message.To)
            && message.To != "*"
            && !message.To.StartsWith('#')
            && message.To.Any(char.IsLetter);   // groups are purely numeric → no letters

        // Only flag as "direct to this node" when the destination callsign matches
        // the node's configured callsign OR the primary legacy callsign.
        // This prevents foreign direct messages relayed via LoRa from triggering AutoReply.
        bool isDirectToNode = nodeId is not null
            && looksLikeCallsign
            && (string.Equals(message.To, myCallsign, StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.To, _settings.MyCallsign, StringComparison.OrdinalIgnoreCase));

        if (message.IsBroadcast)
        {
            bool addressedToUs = string.Equals(message.To, myCallsign, StringComparison.OrdinalIgnoreCase);
            tabKey = addressedToUs && !string.IsNullOrEmpty(message.From) && state.Tabs.ContainsKey(message.From)
                ? message.From
                : "*";
        }
        else if (string.Equals(message.To, myCallsign, StringComparison.OrdinalIgnoreCase) || isDirectToNode)
        {
            // Direct message to this node (regardless of whether NodeProfile.Callsign matches exactly)
            tabKey = message.From;
        }
        else
        {
            tabKey = "#" + message.To;
        }

        // For group messages, only auto-create a tab if the filter is disabled or the group is whitelisted.
        bool isGroup = tabKey.StartsWith('#');
        bool tabAllowed = !isGroup
            || !_settings.GroupFilterEnabled
            || _settings.Groups.Contains(tabKey, StringComparer.OrdinalIgnoreCase);

        // MH-Liste und Karte werden ausschließlich vom Primary-Node befüllt.
        var primaryState = GetPrimaryState();
        bool mhChanged = IsPrimaryNode(nodeId) && UpdateMhList(message, primaryState);

        ChatTab? tab = null;
        bool wasNewDirect = false;
        if (tabAllowed)
            tab = GetOrCreateTab(state, tabKey, nodeId, out wasNewDirect);
        lock (_lock)
        {
            AppendToMonitor(message, state);
            if (tab != null)
            {
                AppendToTab(tab, message);
                tab.UnreadCount++;
            }
        }

        if (wasNewDirect)
            OnNewDirectTab?.Invoke(message.From, message);

        bool isDirectToUs = !message.IsBroadcast &&
            !message.IsAck && !message.IsPositionBeacon && !message.IsTelemetry &&
            (string.Equals(message.To, myCallsign, StringComparison.OrdinalIgnoreCase) || isDirectToNode);
        if (isDirectToUs && !wasNewDirect)
            OnDirectMessage?.Invoke(message.From, message);

        if (wasNewDirect && tab != null)
            _ = CheckQsoSummaryAsync(tab, tabKey);

        if (mhChanged) OnMhChange?.Invoke();
        NotifyChange();
        _ = _webhook.SendAsync(message, "message");
        _ = _mqtt?.PublishAsync(message, "message");
        CheckWatchlist(message);
        CheckCq(message, tabKey, myCallsign);

        if (!message.IsBroadcast &&
            string.Equals(message.To, myCallsign, StringComparison.OrdinalIgnoreCase) &&
            MeshcomWebDesk.Services.Bot.BotCommandService.IsCommand(message.Text))
            OnBotCommand?.Invoke(message);
    }

    /// <summary>Add an outgoing message to the correct tab.</summary>
    public void AddOutgoingMessage(MeshcomMessage message) => AddOutgoingMessage(message, message.NodeId);

    public void AddOutgoingMessage(MeshcomMessage message, Guid? nodeId)
    {
        var state = ResolveState(nodeId);
        var tabKey = message.IsBroadcast ? "*" : message.To;
        var tab = GetOrCreateTab(state, tabKey, nodeId);
        lock (_lock)
        {
            AppendToMonitor(message, state);
            AppendToTab(tab, message);
        }
        NotifyChange();
    }

    /// <summary>Add a message to the raw feed only, without routing it to any tab.</summary>
    public void AddRawMessage(MeshcomMessage message) => AddRawMessage(message, message.NodeId);

    public void AddRawMessage(MeshcomMessage message, Guid? nodeId)
    {
        lock (_lock) { AppendToMonitor(message, ResolveState(nodeId)); }
        NotifyChange();
    }

    /// <summary>Open a new tab manually.</summary>
    public ChatTab OpenTab(string key) => OpenTab(key, null);

    public ChatTab OpenTab(string key, Guid? nodeId)
    {
        var state = ResolveState(nodeId);
        var tab = GetOrCreateTab(state, key, nodeId);
        state.ActiveTabKey = key;
        // Backward-compat: keep legacy ActiveTabKey in sync when operating on primary node
        if (nodeId is null || nodeId == Guid.Empty) ActiveTabKey = key;
        NotifyChange();
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
    public void CloseTab(string key) => CloseTab(key, null);

    public void CloseTab(string key, Guid? nodeId)
    {
        ResolveState(nodeId).Tabs.TryRemove(key, out _);
        NotifyChange();
    }

    /// <summary>Resets the unread counter for the given tab.</summary>
    public void ClearUnread(string key) => ClearUnread(key, null);

    public void ClearUnread(string key, Guid? nodeId)
    {
        if (ResolveState(nodeId).Tabs.TryGetValue(key, out var tab))
            lock (_lock) { tab.UnreadCount = 0; }
    }

    /// <summary>
    /// Assigns the node sequence number (from the echo packet) to the most recent
    /// outgoing message sent to <paramref name="destination"/> that has no sequence yet.
    /// </summary>
    public void AssignOutgoingSequence(string destination, string sequenceNumber) =>
        AssignOutgoingSequence(destination, sequenceNumber, null);

    public void AssignOutgoingSequence(string destination, string sequenceNumber, Guid? nodeId)
    {
        var messages = ResolveState(nodeId).Messages;
        lock (_lock)
        {
            var msg = messages.LastOrDefault(m =>
                m.IsOutgoing &&
                (m.SequenceNumber == null || m.SequenceNumber == "TX") &&
                string.Equals(m.To.TrimStart('#'), destination.TrimStart('#'), StringComparison.OrdinalIgnoreCase));
            if (msg != null)
            {
                msg.SequenceNumber   = sequenceNumber;
                msg.NodeEchoReceived = true;   // Node hat UDP-Paket empfangen und verarbeitet
            }
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
    public void MarkMessageAcknowledged(string sequenceNumber, string? ackSender = null, bool isGateway = false) =>
        MarkMessageAcknowledged(sequenceNumber, null, ackSender, isGateway);

    public void MarkMessageAcknowledged(string sequenceNumber, Guid? nodeId, string? ackSender = null, bool isGateway = false)
    {
        bool Found(IEnumerable<MeshcomMessage> messages)
        {
            lock (_lock)
            {
                var msg = messages.FirstOrDefault(m =>
                    m.IsOutgoing && m.SequenceNumber == sequenceNumber);

                if (msg == null && ackSender != null)
                {
                    var cutoff = DateTime.Now.AddMinutes(-10);
                    msg = messages.FirstOrDefault(m =>
                        m.IsOutgoing &&
                        m.Timestamp >= cutoff &&
                        string.Equals(m.To, ackSender, StringComparison.OrdinalIgnoreCase));
                }

                if (msg != null)
                {
                    msg.SequenceNumber  = sequenceNumber;
                    msg.IsAcknowledged  = true;
                    // Accumulate delivery flags – never clear a flag that was already set.
                    if (isGateway)  msg.IsGatewayDelivered = true;
                    else            msg.IsLoraDelivered    = true;
                    return true;
                }
                return false;
            }
        }

        // Search the node that received the ACK first, then fall back to all other nodes.
        // In multi-node setups the outgoing message may have been sent from a different node
        // than the one that received the ACK (e.g. DH1FR-99 sent Pong, DH1FR-2 ACKs it back
        // and the ACK arrives at DH1FR-2's WebDesk – but the Pong lives in DH1FR-99's state).
        if (!Found(ResolveState(nodeId).Messages))
        {
            foreach (var state in _nodeState.Values)
                if (Found(state.Messages)) break;
        }

        NotifyChange();
    }

    /// <summary>
    /// Processes an incoming APRS ACK: marks the matched outgoing message as delivered,
    /// updates the relay path and signal data for the sending station in the MH list
    /// (so the connection appears on the map), and appends the ACK to the monitor feed.
    /// </summary>
    public void AddAck(MeshcomMessage message) => AddAck(message, message.NodeId);

    public void AddAck(MeshcomMessage message, Guid? nodeId)
    {
        if (message.SequenceNumber != null)
        {
            var isGateway = string.Equals(message.SrcType, "udp", StringComparison.OrdinalIgnoreCase);
            MarkMessageAcknowledged(message.SequenceNumber, nodeId, message.From, isGateway);
        }
        var ackState = ResolveState(nodeId);
        bool ackMhChanged = IsPrimaryNode(nodeId) && UpdateMhList(message, GetPrimaryState());
        lock (_lock) { AppendToMonitor(message, ackState); }
        if (ackMhChanged) OnMhChange?.Invoke();
        NotifyChange();
        CheckWatchlist(message);
    }

    /// <summary>Remove all entries from the MH list (primary node only).</summary>
    public void ClearMhList()
    {
        foreach (var state in _nodeState.Values)
            state.MhList.Clear();
        OnMhChange?.Invoke();
        OnChange?.Invoke();
    }

    /// <summary>
    /// Removes MH list entries whose <c>LastHeard</c> timestamp is older than
    /// <see cref="MeshcomSettings.MhMaxAgeHours"/> hours.
    /// Does nothing when <c>MhMaxAgeHours</c> is 0 (feature disabled).
    /// </summary>
    /// <returns>Number of removed entries.</returns>
    public int PurgeMhListByAge()
    {
        int maxAgeHours = _settings.MhMaxAgeHours;
        if (maxAgeHours <= 0) return 0;

        var cutoff = DateTime.Now.AddHours(-maxAgeHours);
        int total = 0;
        foreach (var state in _nodeState.Values)
        {
            var toRemove = state.MhList.Where(kv => kv.Value.LastHeard < cutoff).Select(kv => kv.Key).ToList();
            foreach (var key in toRemove) state.MhList.TryRemove(key, out _);
            total += toRemove.Count;
        }

        if (total > 0) { OnMhChange?.Invoke(); OnChange?.Invoke(); }
        return total;
    }

    public void RemoveFromMhList(string callsign)
    {
        foreach (var state in _nodeState.Values)
            state.MhList.TryRemove(callsign, out _);
        OnMhChange?.Invoke();
        OnChange?.Invoke();
    }

    /// <summary>Clears all chat tabs, MH list and monitor entries across all nodes.</summary>
    public void ClearAllData()
    {
        lock (_lock)
        {
            foreach (var state in _nodeState.Values)
            {
                state.Tabs.Clear();
                state.MhList.Clear();
                state.Messages.Clear();
            }
            _seenMessageKeys.Clear();
        }
        NotifyChange();
    }

    /// <summary>Creates a thread-safe snapshot of all node states.</summary>
    public PersistenceSnapshot CreateSnapshot()
    {
        var primaryState = GetPrimaryState();
        lock (_lock)
        {
            // Build the legacy primary-node fields (backwards compat)
            var snapshot = new PersistenceSnapshot
            {
                SavedAt         = DateTime.Now,
                Tabs            = primaryState.Tabs.Values
                                    .Select(t => new ChatTab { NodeId = t.NodeId, Key = t.Key, Title = t.Title, Messages = t.Messages.ToList() })
                                    .ToList(),
                MhList          = primaryState.MhList.Values.ToList(),
                MonitorMessages = primaryState.Messages.ToList(),
                TabOrder        = primaryState.TabOrder.ToList()
            };

            // Persist every known node state into NodeSnapshots
            foreach (var (nodeId, state) in _nodeState)
            {
                snapshot.NodeSnapshots[nodeId.ToString()] = new NodeSnapshotEntry
                {
                    Tabs            = state.Tabs.Values
                                        .Select(t => new ChatTab { NodeId = t.NodeId, Key = t.Key, Title = t.Title, Messages = t.Messages.ToList() })
                                        .ToList(),
                    MonitorMessages = state.Messages.ToList(),
                    TabOrder        = state.TabOrder.ToList()
                };
            }

            return snapshot;
        }
    }

    /// <summary>Restores state from a previously saved snapshot into all node states.</summary>
    public void LoadSnapshot(PersistenceSnapshot snapshot)
    {
        lock (_lock)
        {
            // ── Restore per-node data from NodeSnapshots (multi-node format) ──
            foreach (var (nodeIdStr, entry) in snapshot.NodeSnapshots)
            {
                if (!Guid.TryParse(nodeIdStr, out var nodeId)) continue;
                var state = _nodeState.GetOrAdd(nodeId, _ => new NodeState());

                state.Messages.Clear();
                state.Messages.AddRange(entry.MonitorMessages.TakeLast(_settings.MonitorMaxMessages));

                state.Tabs.Clear();
                foreach (var tab in entry.Tabs)
                {
                    bool isGroup   = tab.Key.StartsWith('#');
                    bool tabAllowed = !isGroup
                        || !_settings.GroupFilterEnabled
                        || _settings.Groups.Contains(tab.Key, StringComparer.OrdinalIgnoreCase);
                    if (tabAllowed)
                    {
                        var max = _settings.TabMaxMessages;
                        if (max > 0 && tab.Messages.Count > max)
                            tab.Messages.RemoveRange(0, tab.Messages.Count - max);
                        tab.MessageCount = tab.Messages.Count;
                        state.Tabs[tab.Key] = tab;
                    }
                }

                state.TabOrder = entry.TabOrder.Count > 0 ? [.. entry.TabOrder] : [];
            }

            // ── Restore MH list + primary fallback (legacy single-node snapshots) ──
            var primaryState = GetPrimaryState();

            // MH list is always primary-only
            primaryState.MhList.Clear();
            foreach (var station in snapshot.MhList)
                primaryState.MhList[station.Callsign] = station;

            // If the new NodeSnapshots dict was empty (old snapshot file), fall back to
            // restoring the legacy Tabs/MonitorMessages into the primary state
            if (snapshot.NodeSnapshots.Count == 0)
            {
                primaryState.Messages.Clear();
                primaryState.Messages.AddRange(snapshot.MonitorMessages.TakeLast(_settings.MonitorMaxMessages));

                primaryState.Tabs.Clear();
                foreach (var tab in snapshot.Tabs)
                {
                    bool isGroup   = tab.Key.StartsWith('#');
                    bool tabAllowed = !isGroup
                        || !_settings.GroupFilterEnabled
                        || _settings.Groups.Contains(tab.Key, StringComparer.OrdinalIgnoreCase);
                    if (tabAllowed)
                    {
                        var max = _settings.TabMaxMessages;
                        if (max > 0 && tab.Messages.Count > max)
                            tab.Messages.RemoveRange(0, tab.Messages.Count - max);
                        tab.MessageCount = tab.Messages.Count;
                        primaryState.Tabs[tab.Key] = tab;
                    }
                }

                primaryState.TabOrder = snapshot.TabOrder.Count > 0 ? [.. snapshot.TabOrder] : [];
            }
        }

        NotifyChange();
        PurgeMhListByAge();

        // Trigger QSO summary for all direct-message tabs across every node
        foreach (var (_, state) in _nodeState)
        {
            foreach (var tab in state.Tabs.Values.Where(t => t.Key != "*" && !t.Key.StartsWith('#')))
                _ = CheckQsoSummaryAsync(tab, tab.Key);
        }
    }

    /// <summary>
    /// Process a pure position beacon: update MH position data and add to raw feed.
    /// Does NOT open or update any chat tab.
    /// </summary>
    public void AddPositionBeacon(MeshcomMessage message) => AddPositionBeacon(message, message.NodeId);

    public void AddPositionBeacon(MeshcomMessage message, Guid? nodeId)
    {
        var state = ResolveState(nodeId);
        bool posMhChanged = IsPrimaryNode(nodeId) && UpdateMhList(message, GetPrimaryState());
        lock (_lock) { AppendToMonitor(message, state); }
        if (posMhChanged) OnMhChange?.Invoke();
        NotifyChange();
        _ = _webhook.SendAsync(message, "position");
        _ = _mqtt?.PublishAsync(message, "position");
        CheckWatchlist(message);
    }

    /// <summary>
    /// Process a telemetry packet
    /// Does NOT open or update any chat tab.
    /// </summary>
    public void AddTelemetry(MeshcomMessage message) => AddTelemetry(message, message.NodeId);

    public void AddTelemetry(MeshcomMessage message, Guid? nodeId)
    {
        var state = ResolveState(nodeId);
        bool telMhChanged = IsPrimaryNode(nodeId) && UpdateMhList(message, GetPrimaryState());
        lock (_lock) { AppendToMonitor(message, state); }
        if (telMhChanged) OnMhChange?.Invoke();
        NotifyChange();
        _ = _webhook.SendAsync(message, "telemetry");
        _ = _mqtt?.PublishAsync(message, "telemetry");
        CheckWatchlist(message);
    }

    /// <summary>Get a specific tab.</summary>
    public ChatTab? GetTab(string key) => GetTab(key, null);

    public ChatTab? GetTab(string key, Guid? nodeId)
    {
        ResolveState(nodeId).Tabs.TryGetValue(key, out var tab);
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
    public IReadOnlyList<MeshcomMessage> GetTabMessages(string key) => GetTabMessages(key, null);

    public IReadOnlyList<MeshcomMessage> GetTabMessages(string key, Guid? nodeId)
    {
        if (!ResolveState(nodeId).Tabs.TryGetValue(key, out var tab))
            return [];
        lock (_lock) { return tab.Messages.ToList(); }
    }

    /// <summary>
    /// Returns true when an identical message was already processed within <see cref="DedupWindow"/>.
    /// Registers the message as seen on first encounter.
    /// Priority: msg_id (most reliable) → seq:{From}:{SeqNr} → txt:{From}:{To}:{Text}
    /// The NodeId is included in the key so the same packet received from two different
    /// nodes is NOT considered a duplicate (each node relays its own traffic independently).
    /// </summary>
    private bool IsDuplicate(MeshcomMessage message)
    {
        // Node prefix ensures messages from different nodes are never cross-deduplicated.
        var nodePrefix = message.NodeId?.ToString("N") ?? "legacy";

        string key = !string.IsNullOrEmpty(message.MsgId)
            ? $"{nodePrefix}:mid:{message.MsgId}"
            : !string.IsNullOrEmpty(message.SequenceNumber)
                ? $"{nodePrefix}:seq:{message.From}:{message.SequenceNumber}"
                : $"{nodePrefix}:txt:{message.From}:{message.To}:{message.Text}";

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

    /// <summary>
    /// Updates the MH list from the given message.
    /// Returns <c>true</c> when the map/MH view should be refreshed:
    /// new station, position change, telemetry update, relay path change, or RSSI update.
    /// </summary>
    private bool UpdateMhList(MeshcomMessage message, NodeState state)
    {
        if (string.IsNullOrEmpty(message.From)) return false;
        bool mhChanged = false;
        state.MhList.AddOrUpdate(
            message.From,
            _ =>
            {
                mhChanged = true;
                return new HeardStation
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
                };
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
                if (message.Rssi.HasValue)    { s.LastRssi = message.Rssi;  mhChanged = true; }
                if (message.Snr.HasValue)     { s.LastSnr  = message.Snr;   mhChanged = true; }
                if (message.Battery.HasValue) { s.Battery  = message.Battery; mhChanged = true; }
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
                    mhChanged = true;
                }
                if (message.Latitude.HasValue)
                {
                    s.Latitude         = message.Latitude;
                    s.Longitude        = message.Longitude;
                    s.Altitude         = message.Altitude;
                    s.LastPositionTime = message.Timestamp;
                    mhChanged = true;
                }
                if (message.IsTelemetry)
                {
                    if (message.Temp1.HasValue)    s.Temp1    = message.Temp1;
                    if (message.Humidity.HasValue)  s.Humidity = message.Humidity;
                    if (message.Pressure.HasValue)  s.Pressure = message.Pressure;
                    s.LastTelemetryTime = message.Timestamp;
                    mhChanged = true;
                }
                return s;
            });

        return mhChanged;
    }

    private void AppendToMonitor(MeshcomMessage message, NodeState state)
    {
        state.Messages.Add(message);
        if (state.Messages.Count > _settings.MonitorMaxMessages)
            state.Messages.RemoveRange(0, state.Messages.Count - _settings.MonitorMaxMessages);
        _ = _sink.WriteAsync(message);
    }

    private void AppendToTab(ChatTab tab, MeshcomMessage message)
    {
        tab.Messages.Add(message);
        var max = _settings.TabMaxMessages;
        if (max > 0 && tab.Messages.Count > max)
            tab.Messages.RemoveRange(0, tab.Messages.Count - max);
        tab.MessageCount = tab.Messages.Count;
    }

    private ChatTab GetOrCreateTab(NodeState state, string key, Guid? nodeId, out bool wasNewDirect)
    {
        var newTab = new ChatTab
        {
            NodeId = nodeId,
            Key    = key,
            Title  = key switch { "*" => "Alle", _ => key }
        };
        var tab = state.Tabs.GetOrAdd(key, newTab);
        wasNewDirect = ReferenceEquals(tab, newTab) && key != "*" && !key.StartsWith('#');
        if (wasNewDirect) OnNewTab?.Invoke(key);
        return tab;
    }

    private ChatTab GetOrCreateTab(NodeState state, string key, Guid? nodeId) =>
        GetOrCreateTab(state, key, nodeId, out _);

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

    /// <summary>
    /// Detects CQ calls in group messages and fires <see cref="OnCqHeard"/>.
    /// Rules:
    ///  - Only group messages (tabKey starts with '#') that pass the group filter.
    ///  - Message text must contain "CQ" as a standalone token (case-insensitive).
    ///  - Own callsign is suppressed.
    /// </summary>
    private void CheckCq(MeshcomMessage message, string tabKey, string myCallsign)
    {
        if (!tabKey.StartsWith('#')) return;
        if (string.IsNullOrWhiteSpace(message.Text)) return;
        if (string.IsNullOrWhiteSpace(message.From)) return;
        if (string.Equals(message.From, myCallsign, StringComparison.OrdinalIgnoreCase))
            return;

        // Group filter: only whitelisted groups (same logic as tab routing)
        bool groupAllowed = !_settings.GroupFilterEnabled
            || _settings.Groups.Contains(tabKey, StringComparer.OrdinalIgnoreCase);
        if (!groupAllowed) return;

        if (!CqRegex.IsMatch(message.Text)) return;

        // Extract group number from tabKey (strip '#')
        var group = tabKey.TrimStart('#');
        _logger.LogInformation("CQ detected: From={From} Group={Group} Text={Text}",
            message.From, group, message.Text);
        OnCqHeard?.Invoke(message.From, group, message.Text);
    }

    private void NotifyChange()
    {
        OnChange?.Invoke();
    }
}

