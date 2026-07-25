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

    /// <summary>Returns the state for the primary node (or Guid.Empty in legacy mode).
    /// When transitioning from legacy single-node mode to multi-node (i.e. the first
    /// NodeProfile is added), the existing Guid.Empty bucket is migrated into the new
    /// primary-node bucket so that tabs and messages are preserved.</summary>
    private NodeState GetPrimaryState()
    {
        var primaryId = _nodeManager?.PrimaryNode?.Id;
        if (primaryId is null)
            return GetState(null); // legacy mode → Guid.Empty

        // Multi-node mode: check whether the primary bucket already has data.
        // If not, but Guid.Empty has data (legacy → multi-node transition), migrate it once.
        var primaryState = GetState(primaryId);
        if (primaryState.Tabs.IsEmpty && primaryState.Messages.Count == 0
            && _nodeState.TryGetValue(Guid.Empty, out var legacyState)
            && (!legacyState.Tabs.IsEmpty || legacyState.Messages.Count > 0))
        {
            lock (_lock)
            {
                // Re-check inside lock to avoid double-migration under concurrent access.
                if (primaryState.Tabs.IsEmpty && primaryState.Messages.Count == 0)
                {
                    foreach (var kv in legacyState.Tabs)
                        primaryState.Tabs.TryAdd(kv.Key, kv.Value);
                    primaryState.Messages.AddRange(legacyState.Messages);
                    primaryState.TabOrder = legacyState.TabOrder.ToList();
                    primaryState.ActiveTabKey = legacyState.ActiveTabKey;
                    foreach (var kv in legacyState.MhList)
                        primaryState.MhList.TryAdd(kv.Key, kv.Value);
                    legacyState.Tabs.Clear();
                    legacyState.Messages.Clear();
                    legacyState.MhList.Clear();
                    _logger.LogInformation(
                        "Migrated legacy single-node state (Guid.Empty) into primary node {PrimaryId}",
                        primaryId);
                }
            }
        }
        return primaryState;
    }

    /// <summary>Resolves <paramref name="nodeId"/> to its state bucket:
    /// <c>null</c> → primary node; explicit Guid → that node's bucket.</summary>
    private NodeState ResolveState(Guid? nodeId) =>
        nodeId is null ? GetPrimaryState() : GetState(nodeId);

    /// <summary>Returns the bucket key that <see cref="ResolveState"/> maps <paramref name="nodeId"/> to:
    /// <c>null</c> collapses to the primary node's Id (or <see cref="Guid.Empty"/> in legacy mode),
    /// so packets from unknown IPs and packets from the primary node share one dedup scope.</summary>
    private Guid ResolveBucketId(Guid? nodeId) =>
        nodeId ?? _nodeManager?.PrimaryNode?.Id ?? Guid.Empty;

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
    /// Every message is registered under two kinds of keys:
    /// a global key ("mid:…" / "seq:{From}:{SeqNr}" / "txt:{From}:{To}:{Text}") that tracks
    /// whether ANY node already delivered the message (gates one-time side effects), and the
    /// same key prefixed with the state-bucket Guid that tracks whether THIS node's bucket
    /// already stored it (gates tab/monitor insertion, so each node keeps its own copy).
    /// Value = time of first receipt. Entries older than <see cref="DedupWindow"/> are pruned on each check.
    /// </summary>
    private readonly Dictionary<string, DateTime> _seenMessageKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Outgoing pings awaiting a "Pong!" reply, keyed by "{bucketId}:{partner}" → send timestamp.
    /// Used to compute the round-trip time shown on the matching incoming Pong (see <see cref="AddIncomingMessage"/>).
    /// Entries older than <see cref="PingTimeout"/> are ignored and overwritten by the next ping.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _pendingPings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromMinutes(5);

    private static string PendingPingKey(Guid bucketId, string partner) => $"{bucketId}:{partner.Trim()}";

    /// <summary>True for the bare ping command in any of its accepted forms ("ping", "--ping", ">ping").</summary>
    private static bool IsPingText(string text)
    {
        var t = text.Trim();
        return t.Equals("ping", StringComparison.OrdinalIgnoreCase)
            || t.Equals("--ping", StringComparison.OrdinalIgnoreCase)
            || t.Equals(">ping", StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>Sort column key for the MH list view. Persists across navigation.</summary>
    public string MhSortColumn { get; set; } = "LastHeard";
    /// <summary>Sort direction for the MH list view. Persists across navigation.</summary>
    public bool MhSortAscending { get; set; } = false;

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
        // mesh routes. Scoped per state bucket so every node that heard the message keeps its
        // own copy; isFirstReceipt marks the very first copy across all nodes and gates
        // one-time side effects (webhook, MQTT, watchlist, CQ) below.
        if (IsDuplicate(message, ResolveBucketId(nodeId), out bool isFirstReceipt))
        {
            _logger.LogDebug("Duplicate message suppressed: Node={NodeId} From={From} Seq={Seq} Text={Text}",
                nodeId, message.From, message.SequenceNumber, message.Text);
            return;
        }

        // Resolve the own callsign for this node
        var myCallsign = _nodeManager?.GetCallsignForNode(nodeId) ?? _settings.MyCallsign;

        // Match a Pong reply to a pending outgoing ping and compute the round-trip time.
        if (!message.IsBroadcast && !string.IsNullOrEmpty(message.From)
            && message.Text.TrimStart().StartsWith("Pong!", StringComparison.OrdinalIgnoreCase))
        {
            var pingKey = PendingPingKey(ResolveBucketId(nodeId), message.From);
            if (_pendingPings.TryRemove(pingKey, out var sentAt))
            {
                var rtt = message.Timestamp - sentAt;
                if (rtt >= TimeSpan.Zero && rtt <= PingTimeout)
                    message.PingRoundTrip = rtt;
            }
        }

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
        // the node's configured callsign.
        // Do NOT fall back to _settings.MyCallsign here – in multi-node setups that would
        // cause messages addressed to the primary node's callsign to open a direct tab on
        // every other node as well (e.g. a message to DH1FR-2 appearing on DH1FR-99).
        bool isDirectToNode = nodeId is not null
            && looksLikeCallsign
            && string.Equals(message.To, myCallsign, StringComparison.OrdinalIgnoreCase);

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
            // Guard: From can be null/empty for malformed packets – fall back to "*" (Alle) tab
            tabKey = !string.IsNullOrEmpty(message.From) ? message.From : "*";
        }
        else
        {
            // Guard: To can be null for malformed packets – fall back to "*" (Alle) tab
            tabKey = !string.IsNullOrEmpty(message.To) ? "#" + message.To : "*";
        }

        // For group messages, only auto-create a tab if the filter is disabled or the group is whitelisted.
        bool isGroup = tabKey.StartsWith('#');
        bool tabAllowed = !isGroup
            || !_settings.GroupFilterEnabled
            || _settings.Groups.Contains(tabKey, StringComparer.OrdinalIgnoreCase);

        // A direct message addressed to a sibling node's callsign (e.g. To=DH1FR-2 heard on RF
        // by DH1FR-99) falls into the group branch above and would open a pseudo-group tab
        // "#DH1FR-2" here. Suppress the tab – the message still appears in this node's monitor.
        if (isGroup && looksLikeCallsign && _nodeManager is not null &&
            _nodeManager.Nodes.Any(n => string.Equals(n.Callsign, message.To, StringComparison.OrdinalIgnoreCase)))
            tabAllowed = false;

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

        // One-time side effects: fire only for the first copy across all nodes, otherwise a
        // message heard by two nodes would trigger duplicate webhooks/notifications.
        // OnBotCommand/OnDirectMessage below need no gate – their To==myCallsign check already
        // limits them to the one node the message was addressed to.
        if (isFirstReceipt)
        {
            _ = _webhook.SendAsync(message, "message");
            _ = _mqtt?.PublishAsync(message, "message");
            CheckWatchlist(message);
            CheckCq(message, tabKey, myCallsign);
        }

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

        // Track direct pings so the round-trip time can be shown on the partner's Pong reply.
        if (!message.IsBroadcast && !message.To.StartsWith('#') && IsPingText(message.Text))
            _pendingPings[PendingPingKey(ResolveBucketId(nodeId), message.To)] = message.Timestamp;

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
    public void AssignOutgoingSequence(string destination, string? sequenceNumber) =>
        AssignOutgoingSequence(destination, sequenceNumber, null, null);

    public void AssignOutgoingSequence(string destination, string? sequenceNumber, Guid? nodeId) =>
        AssignOutgoingSequence(destination, sequenceNumber, nodeId, null);

    public void AssignOutgoingSequence(string destination, string? sequenceNumber, Guid? nodeId, string? viaPath)
    {
        bool Found(IEnumerable<MeshcomMessage> messages)
        {
            lock (_lock)
            {
                var msg = messages.LastOrDefault(m =>
                    m.IsOutgoing &&
                    (m.SequenceNumber == null || m.SequenceNumber == "TX") &&
                    string.Equals(m.To.TrimStart('#'), destination.TrimStart('#'), StringComparison.OrdinalIgnoreCase));
                if (msg == null) return false;
                // Group echoes have no {NNN} – preserve existing sequence number rather than overwriting with null
                if (sequenceNumber != null)
                    msg.SequenceNumber = sequenceNumber;
                msg.NodeEchoReceived = true;
                // The node echo is the first point where the actual via-routing becomes known
                if (viaPath != null)
                    msg.ViaPath = viaPath;
                return true;
            }
        }

        // Search the node whose echo arrived first, then fall back to all other nodes.
        // A relay echo (src_type:"lora", src=myCallsign) may arrive from a sibling node's
        // IP, so its nodeId key differs from the state bucket that holds the outgoing message.
        if (!Found(ResolveState(nodeId).Messages))
        {
            foreach (var state in _nodeState.Values)
                if (Found(state.Messages)) break;
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
        MarkMessageAcknowledged(sequenceNumber, null, ackSender, isGateway, DateTime.Now);

    public void MarkMessageAcknowledged(string sequenceNumber, Guid? nodeId, string? ackSender = null, bool isGateway = false, DateTime? ackTimestamp = null)
    {
        var ackedAt = ackTimestamp ?? DateTime.Now;

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
                    // An ACK proves the node received and transmitted the message – clear warning triangle.
                    msg.NodeEchoReceived = true;
                    // Accumulate delivery flags – never clear a flag that was already set.
                    if (isGateway)  msg.IsGatewayDelivered = true;
                    else            msg.IsLoraDelivered    = true;
                    // Set once, on the first ACK – a later second ACK (e.g. gateway after LoRa) must not overwrite it.
                    var rtt = ackedAt - msg.Timestamp;
                    if (rtt >= TimeSpan.Zero)
                        msg.AckRoundTrip ??= rtt;
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
            MarkMessageAcknowledged(message.SequenceNumber, nodeId, message.From, isGateway, message.Timestamp);
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
                    if (string.IsNullOrEmpty(tab.Key)) continue;
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
            {
                // Snapshots from before RelayPathHistory existed only carry LastRelayPath;
                // seed the history from it so relay lines appear right after restart.
                if (station.RelayPathHistory.Count == 0 && !string.IsNullOrEmpty(station.LastRelayPath))
                {
                    station.RelayPathHistory.Add(new RelayPathStat
                    {
                        Path     = station.LastRelayPath,
                        Count    = Math.Max(station.RelayPathCount, 1),
                        LastSeen = station.LastHeard
                    });
                }
                primaryState.MhList[station.Callsign] = station;
            }

            // If the new NodeSnapshots dict was empty (old snapshot file), fall back to
            // restoring the legacy Tabs/MonitorMessages into the primary state
            if (snapshot.NodeSnapshots.Count == 0)
            {
                primaryState.Messages.Clear();
                primaryState.Messages.AddRange(snapshot.MonitorMessages.TakeLast(_settings.MonitorMaxMessages));

                primaryState.Tabs.Clear();
                foreach (var tab in snapshot.Tabs)
                {
                    if (string.IsNullOrEmpty(tab.Key)) continue;
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
        if (string.IsNullOrEmpty(key)) return [];
        if (!ResolveState(nodeId).Tabs.TryGetValue(key, out var tab))
            return [];
        lock (_lock) { return tab.Messages.ToList(); }
    }

    /// <summary>
    /// Returns true when this node's state bucket already stored an identical message within
    /// <see cref="DedupWindow"/>. Registers the message as seen on first encounter.
    /// Priority: msg_id (most reliable) → seq:{From}:{SeqNr} → txt:{From}:{To}:{Text}
    /// <para>
    /// Deduplication is scoped per state bucket (<paramref name="bucketId"/>): the same RF
    /// packet forwarded by two different nodes is stored once in <em>each</em> node's bucket,
    /// so every node processes its traffic independently. Repeated deliveries into the same
    /// bucket (mesh routing, node/lora double copies) are dropped as before.
    /// <paramref name="isFirstReceipt"/> is true only for the very first copy across all
    /// buckets – the caller uses it to fire global one-time side effects exactly once.
    /// </para>
    /// </summary>
    private bool IsDuplicate(MeshcomMessage message, Guid bucketId, out bool isFirstReceipt)
    {
        // msg_id, seq number, and text are sender-assigned and identify the message globally;
        // the bucket prefix scopes storage deduplication to one node's state.
        string key = !string.IsNullOrEmpty(message.MsgId)
            ? $"mid:{message.MsgId}"
            : !string.IsNullOrEmpty(message.SequenceNumber)
                ? $"seq:{message.From}:{message.SequenceNumber}"
                : $"txt:{message.From}:{message.To}:{message.Text}";

        // Text-based fallback key: same content may arrive once with msg_id and once without.
        // Storing and checking both keys catches such cross-format duplicates.
        string? txtKey = (!string.IsNullOrEmpty(message.From) &&
                          !string.IsNullOrEmpty(message.To)   &&
                          !string.IsNullOrEmpty(message.Text))
            ? $"txt:{message.From}:{message.To}:{message.Text}"
            : null;

        var bucketPrefix = bucketId.ToString("N");

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

            isFirstReceipt = !_seenMessageKeys.ContainsKey(key) &&
                             (txtKey == null || !_seenMessageKeys.ContainsKey(txtKey));

            if (_seenMessageKeys.ContainsKey($"{bucketPrefix}:{key}"))
                return true;
            if (txtKey != null && _seenMessageKeys.ContainsKey($"{bucketPrefix}:{txtKey}"))
                return true;

            _seenMessageKeys[key] = now;
            _seenMessageKeys[$"{bucketPrefix}:{key}"] = now;
            if (txtKey != null && txtKey != key)
            {
                _seenMessageKeys[txtKey] = now;
                _seenMessageKeys[$"{bucketPrefix}:{txtKey}"] = now;
            }
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
                var created = new HeardStation
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
                if (message.RelayPath is not null)
                    RecordRelayPath(created, message.RelayPath, message.Timestamp);
                return created;
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
                    RecordRelayPath(s, message.RelayPath, message.Timestamp);
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

    /// <summary>
    /// Records a relay path observation in the station's bounded path history
    /// (least recently seen path is evicted when the cap is reached).
    /// </summary>
    private static void RecordRelayPath(HeardStation s, string path, DateTime timestamp)
    {
        lock (s.RelayPathHistory)
        {
            var stat = s.RelayPathHistory.FirstOrDefault(p => p.Path == path);
            if (stat is null)
            {
                if (s.RelayPathHistory.Count >= HeardStation.MaxRelayPathHistory)
                {
                    var oldest = s.RelayPathHistory.MinBy(p => p.LastSeen);
                    if (oldest is not null) s.RelayPathHistory.Remove(oldest);
                }
                stat = new RelayPathStat { Path = path };
                s.RelayPathHistory.Add(stat);
            }
            stat.Count++;
            stat.LastSeen = timestamp;
        }
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

