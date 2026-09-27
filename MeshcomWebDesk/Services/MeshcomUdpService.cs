using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Helpers;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.Bot;
using static MeshcomWebDesk.Helpers.MessageValidator;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Background service that handles UDP communication with a MeshCom device.
/// Listens for incoming messages and provides a method to send messages.
/// 
/// MeshCom EXTUDP JSON format:
///   RX chat : {"src_type":"lora","type":"msg","src":"NOCALL-1","dst":"NOCALL-2","msg":"Hello{034","rssi":-95,"snr":12,...}
///   RX pos  : {"src_type":"node","type":"pos","src":"NOCALL-2","lat":50.8515,"lat_dir":"N","long":9.1075,"long_dir":"E","alt":827,...}
///   TX chat : {"type":"msg","dst":"NOCALL-1","msg":"Hello"}
/// </summary>
public partial class MeshcomUdpService : BackgroundService, IMeshcomSender, IMeshcomVariableExpander
{
    private readonly ILogger<MeshcomUdpService> _logger;
    private readonly ChatService _chatService;
    private readonly QrzService  _qrzService;
    private readonly BotCommandService _botCommandService;
    private readonly QsoSummaryService _qsoSummaryService;
    private readonly NodeManager _nodeManager;
    private readonly SettingsService _settingsService;
    private MeshcomSettings _settings;
    private UdpClient? _udpClient;

    /// <summary>
    /// KISS transport, injected after construction (<see cref="SetKissTransport"/>) to avoid a
    /// DI cycle. When a target node uses <see cref="NodeTransport.Kiss"/>, sends are routed here.
    /// </summary>
    private Kiss.KissClientService? _kiss;

    /// <summary>Wires the KISS transport in after the container is built (see Program.cs).</summary>
    public void SetKissTransport(Kiss.KissClientService kiss) => _kiss = kiss;

    /// <summary>
    /// (extudp field name, value) pairs from the last successfully sent extudp "tele" telegram.
    /// Used by <see cref="CheckAndSendExtUdpIfChangedAsync"/> to detect changes; <c>null</c> means
    /// nothing has been sent yet in this process (not persisted across restarts).
    /// </summary>
    private List<(string Field, double Value)>? _lastExtUdpSentValues;

    /// <summary>
    /// Values (restricted to the subset of <see cref="RoleToExtUdpField"/> that WebDesk can also
    /// parse back out of a telemetry echo: temp/hum/temp2 – pressure is excluded because the
    /// node's own qnh/qfe preference logic makes a clean round-trip comparison ambiguous) from the
    /// most recent extudp "tele" send, awaiting confirmation via the node's own next telemetry
    /// echo. Set to <c>null</c> once confirmed or once <see cref="ExtUdpProbeTimeout"/> elapses.
    /// </summary>
    private List<(string Field, double Value)>? _pendingExtUdpProbe;

    /// <summary>UTC send time of <see cref="_pendingExtUdpProbe"/>, for the timeout check.</summary>
    private DateTime _pendingExtUdpProbeSentAt;

    /// <summary>
    /// How long to wait for the node's own telemetry echo before concluding the firmware does not
    /// support the extudp "tele" interface. The node re-beacons its position immediately after
    /// accepting the values, so this only needs to cover UDP/LoRa round-trip latency.
    /// </summary>
    private static readonly TimeSpan ExtUdpProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Maps <see cref="TelemetryMappingEntry.WeatherRole"/> to the fixed field name the node's
    /// <c>handleExternTelemetry()</c> recognizes in the native extudp "tele" JSON. Roles not
    /// listed here (i.e. an empty role) are not sent via extudp.
    /// </summary>
    private static readonly Dictionary<string, string> RoleToExtUdpField = new()
    {
        ["temp"]     = "temp",
        ["humidity"] = "hum",
        ["pressure"] = "press",
        ["temp2"]    = "temp2",
        ["qnh"]      = "qnh",
        ["gasres"]   = "gasres",
        ["co2"]      = "co2",
    };

    /// <summary>UTC timestamp of the last successful extudp send, for the min-interval throttle.</summary>
    private DateTime? _lastExtUdpSentTime;

    /// <summary>
    /// Tracks whether the last per-minute extudp check already logged a "no values available"
    /// warning, so <see cref="CheckAndSendExtUdpIfChangedAsync"/> logs the condition once when it
    /// starts (and once when it clears) instead of spamming the log every minute it persists.
    /// </summary>
    private bool _lastExtUdpBuildFailed;

    /// <summary>Assembly version, resolved once at startup (e.g. "1.4.1").</summary>
    private static readonly string AppVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

    /// <summary>Live connection and statistics status. Updated on every relevant event.</summary>
    public ConnectionStatus Status { get; } = new();

    /// <summary>UTC time the last ext-udp packet was attributed to a given node, by node id.
    /// Lets the UI show a separate "ext-udp alive" indicator next to the KISS one – on a KISS
    /// node ext-udp still carries the node's own position / telemetry / firmware, which KISS
    /// never sends.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastExtUdpRxUtc = new();

    /// <summary>UTC time of the last ext-udp packet from <paramref name="nodeId"/>, or null if none seen.</summary>
    public DateTime? LastExtUdpRxUtc(Guid nodeId) =>
        _lastExtUdpRxUtc.TryGetValue(nodeId, out var t) ? t : null;

    /// <summary>Raised whenever <see cref="Status"/> changes so UI components can refresh.</summary>
    public event Action? OnStatusChange;

    /// <summary>Matches trailing MeshCom sequence markers like {034, {333 at end of message text.</summary>
    [GeneratedRegex(@"\{\d+$")]
    private static partial Regex TrailingSequencePattern();

    /// <summary>
    /// Matches ACK/REJ messages in three formats:
    ///   APRS pure     : "ack187" / "rej187" (standard APRS clients: PinPoint, Direwolf, …)
    ///   APRS-style    : "NOCALL-2 :ack187" (space-padded addressee)
    ///   MeshCom inline : "NOCALL-2:ack187" (callsign immediately followed by :ackNNN)
    /// </summary>
    [GeneratedRegex(@"^(?:\S+\s*:)?(?:ack|rej)\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex AckPattern();

    /// <summary>Captures the sequence number from a trailing {NNN} marker, e.g. "{034" → "034".</summary>
    [GeneratedRegex(@"\{(\d+)$")]
    private static partial Regex SequenceNumberPattern();

    /// <summary>Captures the sequence number from an ACK text: "NOCALL-2:ack034" or bare "ack034" → "034".</summary>
    [GeneratedRegex(@"(?:ack|rej)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AckSequencePattern();

    /// <summary>Captures the target callsign from a MeshCom inline ACK, e.g. "DL3DCW-12:ack881" → "DL3DCW-12".
    /// Does not match the bare APRS form "ack881" (there the JSON dst is the real target).</summary>
    [GeneratedRegex(@"^(\S+?)\s*:(?:ack|rej)\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex AckTargetPattern();

    /// <summary>Detects MeshCom network time-sync broadcasts, e.g. "{CET}2026-04-07 18:11:58".</summary>
    [GeneratedRegex(@"^\{[A-Z]{2,5}\}\d{4}-\d{2}-\d{2}")]
    private static partial Regex TimeSyncPattern();

    public MeshcomUdpService(
        ILogger<MeshcomUdpService> logger,
        IOptionsMonitor<MeshcomSettings> settings,
        ChatService chatService,
        QrzService qrzService,
        BotCommandService botCommandService,
        QsoSummaryService qsoSummaryService,
        NodeManager nodeManager,
        SettingsService settingsService)
    {
        _logger              = logger;
        _chatService         = chatService;
        _qrzService          = qrzService;
        _botCommandService   = botCommandService;
        _qsoSummaryService   = qsoSummaryService;
        _nodeManager         = nodeManager;
        _settingsService     = settingsService;
        _settings            = settings.CurrentValue;
        settings.OnChange(s =>
        {
            var prev = _settings;
            _settings = s;
            _logger.LogInformation("Settings reloaded from appsettings.json.");

            // Re-register with devices if the node list changed so newly-added nodes
            // start forwarding UDP data without requiring an app restart.
            bool nodesChanged = prev.Nodes.Count != s.Nodes.Count
                || prev.Nodes.Zip(s.Nodes).Any(p =>
                    p.First.Id         != p.Second.Id         ||
                    p.First.DeviceIp   != p.Second.DeviceIp   ||
                    p.First.DevicePort != p.Second.DevicePort ||
                    p.First.Enabled    != p.Second.Enabled);
            if (nodesChanged)
                _ = RegisterWithDeviceAsync();

            // Re-arm the firmware-capability check when the user turns extudp telemetry back on
            // (e.g. after flashing supporting firmware), instead of leaving a stale "not
            // confirmed" status showing until the next send resolves it.
            if (!prev.TelemetryExtUdpEnabled && s.TelemetryExtUdpEnabled)
            {
                Status.ExtUdpTelemetryConfirmed = null;
                NotifyStatusChange();
            }
        });

        _chatService.OnNewDirectTab += (callsign, msg) => _ = SendAutoReplyAsync(callsign, msg.Timestamp, msg.NodeId);
        _chatService.OnBotCommand   += msg      => _ = HandleBotCommandAsync(msg);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MeshCom UDP service starting – listening on {Ip}:{Port}, device at {DevIp}:{DevPort}",
            _settings.ListenIp, _settings.ListenPort, _settings.DeviceIp, _settings.DevicePort);

        try
        {
            var localEp = new IPEndPoint(IPAddress.Parse(_settings.ListenIp), _settings.ListenPort);
            _udpClient = new UdpClient(localEp);
            // Disable IPv4-mapped IPv6 (dual-stack) so RemoteEndPoint always returns plain IPv4 addresses.
            // This ensures ResolveNodeByIp can match NodeProfile.DeviceIp entries reliably.
            if (_udpClient.Client.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                _udpClient.Client.DualMode = false;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to bind UDP socket on {Ip}:{Port}", _settings.ListenIp, _settings.ListenPort);
            return;
        }

        Status.IsListening = true;
        NotifyStatusChange();

        // Send registration packet so the device adds this client to its sender list.
        // Without this, the device does not know where to deliver UDP data.
        await RegisterWithDeviceAsync();

        // Beacon task runs permanently and checks BeaconEnabled on each tick.
        _ = RunBeaconAsync(stoppingToken);

        // Telemetry task runs permanently and checks TelemetryEnabled on each tick.
        _ = RunTelemetryAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(stoppingToken);
                    var raw = Encoding.UTF8.GetString(result.Buffer).TrimEnd('\r', '\n');

                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    _logger.LogDebug("UDP RX [{Remote}]: {Data}", result.RemoteEndPoint, raw);
                    if (_settings.LogUdpTraffic)
                        _logger.LogInformation("[UDP-RX] {Remote} {Data}", result.RemoteEndPoint, raw);

                    // Resolve which node sent this packet by its source IP.
                    // This is the only reliable way to distinguish nodes when all share the same UDP port.
                    var sourceIp   = result.RemoteEndPoint.Address;
                    var sourceNode = _nodeManager.ResolveNodeByIp(sourceIp);
                    var myCallsign = sourceNode?.Callsign ?? _settings.MyCallsign;

                    // Update last-seen timestamp so the UI can show online status
                    if (sourceNode is not null)
                        _nodeManager.MarkNodeSeen(sourceNode.Id);

                    if (sourceNode is not null)
                        _logger.LogDebug("UDP RX from node '{NodeName}' ({NodeId}) IP={Ip}",
                            sourceNode.Name, sourceNode.Id, sourceIp);
                    else
                        _logger.LogDebug("UDP RX from unknown node IP={Ip} – using legacy callsign '{Callsign}'",
                            sourceIp, myCallsign);

                    var message = ParseMessage(raw);

                    if (message != null)
                    {
                        // Tag the message with the node it arrived from
                        message.NodeId = sourceNode?.Id;

                        // Record that ext-udp is alive for this node (feeds the "ext-udp" health
                        // dot next to the KISS one). We do NOT drop ext-udp frames from a KISS
                        // node any more: KISS can be a thin/unreliable path (the firmware never
                        // sends telemetry-only frames over it, and a DM addressed to the node is
                        // not relayed either), so dropping the ext-udp copy silently loses
                        // messages. ChatService dedup (msg_id + text-fallback key) collapses the
                        // KISS/ext-udp doubles – and SrcInfo (0x20) now keeps the callsigns
                        // consistent so that dedup actually matches.
                        if (sourceNode is not null)
                        {
                            var extUdpWasStale = !_lastExtUdpRxUtc.TryGetValue(sourceNode.Id, out var prevExtUdp)
                                                 || DateTime.UtcNow - prevExtUdp > TimeSpan.FromMinutes(1);
                            _lastExtUdpRxUtc[sourceNode.Id] = DateTime.UtcNow;
                            if (extUdpWasStale && sourceNode.Transport == NodeTransport.Kiss)
                                NotifyStatusChange();
                        }

                        // Update signal stats from LoRa metadata
                        if (message.Rssi.HasValue)
                        {
                            Status.LastRssi = message.Rssi;
                            Status.LastSnr = message.Snr;
                        }

                        // Skip src_type:"node" relay echoes from foreign callsigns for regular messages.
                        // The node forwards every received LoRa packet twice – once as src_type:"node"
                        // (relay confirmation) and once as src_type:"lora" (the authoritative copy).
                        // Processing the node copy first would occupy the msg_id in the dedup cache and
                        // cause the real lora packet to be dropped as a duplicate.
                        // EXCEPTION: ACKs must never be skipped – the firmware may deliver an ACK only
                        // as src_type:"node" (no second lora copy), so skipping it loses the delivery tick.
                        if (message.IsNodePacket &&
                            !message.IsAck &&
                            !string.Equals(message.From, myCallsign, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Skipping node relay echo from foreign station {From} (src_type=node)", message.From);
                        }
                        else if (string.Equals(message.From, myCallsign, StringComparison.OrdinalIgnoreCase))
                        {
                            if (message.Latitude.HasValue && message.Longitude.HasValue)
                            {
                                SetOwnPosition(message.Latitude.Value, message.Longitude.Value,
                                               message.Altitude, "Node");
                            }
                            // Assign node-assigned sequence number to matching outgoing message.
                            // Group messages echo back without {NNN}, so SequenceNumber may be null –
                            // call AssignOutgoingSequence unconditionally to still set NodeEchoReceived.
                            // message.To is already the final destination (via-nodes are stripped
                            // into ViaPath during parsing); forward ViaPath so the original TX row
                            // in the monitor can be updated with the routing the node chose.
                            _chatService.AssignOutgoingSequence(message.To, message.SequenceNumber, message.NodeId, message.ViaPath);
                            // Capture node firmware + hardware from src_type:"node" packets
                            bool metaChanged = false;
                            if (!string.IsNullOrEmpty(message.Firmware) && Status.NodeFirmware != message.Firmware)
                            {
                                Status.NodeFirmware = message.Firmware;
                                metaChanged = true;
                            }
                            if (message.HwId.HasValue && Status.NodeHwId != message.HwId)
                            {
                                Status.NodeHwId = message.HwId;
                                metaChanged = true;
                            }
                            if (message.Battery.HasValue && Status.OwnBattery != message.Battery)
                            {
                                Status.OwnBattery = message.Battery;
                                metaChanged = true;
                            }
                            // Status is a single app-wide instance (last writer wins across nodes), so
                            // also record the battery per node – needed for the multi-node switcher UI.
                            if (message.Battery.HasValue && sourceNode is not null)
                                _nodeManager.SetNodeBattery(sourceNode.Id, message.Battery.Value);
                            if (message.HwId.HasValue && sourceNode is not null)
                                _nodeManager.SetNodeHwId(sourceNode.Id, message.HwId.Value);
                            _logger.LogDebug("Node echo meta: firmware={Fw} hw_id={HwId} batt={Batt} NodeFirmware={NodeFw} NodeHwId={NodeHwId}",
                                message.Firmware, message.HwId, message.Battery, Status.NodeFirmware, Status.NodeHwId);
                            if (metaChanged) NotifyStatusChange();

                            // Own pos/tele broadcasts are generated autonomously by the node
                            // firmware (not via SendMessageAsync), so unlike own "msg" echoes
                            // there is no pre-existing TX row for them in the monitor – add
                            // them like any other station's beacon instead of skipping.
                            if (message.IsPositionBeacon)
                            {
                                Status.RxCount++;
                                Status.LastRxTime = message.Timestamp;
                                Status.LastRxFrom = message.From;
                                NotifyStatusChange();
                                _chatService.AddPositionBeacon(message);
                            }
                            else if (message.IsTelemetry)
                            {
                                Status.RxCount++;
                                Status.LastRxTime = message.Timestamp;
                                Status.LastRxFrom = message.From;
                                NotifyStatusChange();
                                _chatService.AddTelemetry(message);
                                EvaluateExtUdpProbe(message);
                            }
                            else
                            {
                                _logger.LogDebug("Skipping node echo from {From}", message.From);
                                // Do not add msg/other echoes to the monitor – the TX entry is already shown there.
                            }
                        }
                        else if (message.IsTimeSync)
                        {
                            // Time-sync broadcast: monitor only, no chat tab
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddRawMessage(message);
                        }
                        else if (message.IsAck)
                        {
                            // APRS ACK – mark as delivered, update relay path in MH list, add to monitor
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddAck(message);
                        }
                        else if (message.IsPositionBeacon)
                        {
                            // Pure position beacon: update MH list but don't open a chat tab
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddPositionBeacon(message);
                        }
                        else if (message.IsTelemetry)
                        {
                            // Telemetry packet: update MH list, show in monitor only
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddTelemetry(message);
                        }
                        else
                        {
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddIncomingMessage(message);
                        }
                    }
                    else
                    {
                        // Unparseable data (status, telemetry, etc.) – raw feed only, no tab
                        _chatService.AddRawMessage(new MeshcomMessage
                        {
                            Text   = raw,
                            RawData = raw,
                            NodeId = sourceNode?.Id
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving UDP data");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        finally
        {
            _udpClient.Dispose();
            _udpClient = null;
            Status.IsListening = false;
            Status.IsRegistered = false;
            NotifyStatusChange();
            _logger.LogInformation("MeshCom UDP service stopped");
        }
    }

    /// <summary>
    /// Send a registration packet to the MeshCom device so it adds this client
    /// <summary>
    /// Send a registration packet to every configured MeshCom device so each one adds
    /// this client to its UDP sender list and starts delivering data.
    /// In legacy single-node mode only the primary device is registered.
    /// </summary>
    private async Task RegisterWithDeviceAsync()
    {
        if (_udpClient == null) return;

        // Build the list of (ip, port, callsign) tuples to register with.
        // Multi-node: one entry per NodeProfile.
        // Legacy: single entry from top-level settings.
        var targets = _nodeManager.Nodes.Count > 0
            ? _nodeManager.Nodes.Select(n => (n.DeviceIp, n.DevicePort, n.Callsign, n.Id as Guid?)).ToList()
            : new List<(string, int, string, Guid?)>
              { (_settings.DeviceIp, _settings.DevicePort, _settings.MyCallsign, (Guid?)null) };

        foreach (var (ip, port, callsign, nodeId) in targets)
        {
            try
            {
                var json  = JsonSerializer.Serialize(new { type = "info", src = callsign });
                var bytes = Encoding.UTF8.GetBytes(json);
                var remoteEp = new IPEndPoint(IPAddress.Parse(ip), port);

                await _udpClient.SendAsync(bytes, bytes.Length, remoteEp);
                _logger.LogInformation("UDP registration packet sent to {DeviceIp}:{DevicePort} as {Callsign}",
                    ip, port, callsign);
                if (_settings.LogUdpTraffic)
                    _logger.LogInformation("[UDP-TX] {Remote} {Data}", remoteEp, json);

                _chatService.AddRawMessage(new MeshcomMessage
                {
                    From       = callsign,
                    IsOutgoing = true,
                    Text       = json,
                    RawData    = json,
                    NodeId     = nodeId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send UDP registration packet to {DeviceIp}:{DevicePort}", ip, port);
            }
        }

        Status.IsRegistered = true;
        NotifyStatusChange();
    }

    /// <summary>
    /// Resolves the wire-level destination for a group or callsign.
    /// "#alle" (case-insensitive) and "alle" are mapped to "*" (broadcast)
    /// because there is no real MeshCom group named "alle".
    /// For all other groups the leading '#' is stripped as usual.
    /// </summary>
    private static string ResolveDestination(string group)
    {
        var stripped = group.TrimStart('#');
        return stripped.Equals("alle", StringComparison.OrdinalIgnoreCase) ? "*" : stripped;
    }

    /// <summary>
    /// Returns the normalised chat-tab key for a group setting value.
    /// Broadcast synonyms ("alle", "#alle", "*") are mapped to the "*" tab key
    /// so beacon messages appear in the shared "Alle" broadcast tab.
    /// All other groups get the '#' prefix and are lowercased for consistent display.
    /// e.g. "alle" → "*";  "#Alle" → "*";  "*" → "*";  "#262" → "#262".
    /// </summary>
    private static string ResolveTabKey(string group)
    {
        var stripped = group.TrimStart('#');
        if (stripped.Equals("alle", StringComparison.OrdinalIgnoreCase) || stripped == "*")
            return "*";
        return ("#" + stripped).ToLowerInvariant();
    }

    private async Task SendAutoReplyAsync(string callsign, DateTime triggerTimestamp, Guid? nodeId = null)
    {
        if (!_settings.AutoReplyEnabled || string.IsNullOrWhiteSpace(_settings.AutoReplyText))
            return;

        // Ensure QRZ data is cached before expanding variables so that
        // {dest-name} and {dest-loc} are available for brand-new contacts.
        if (_settings.Qrz.Enabled)
        {
            try { await _qrzService.LookupAsync(callsign); }
            catch (Exception ex) { _logger.LogDebug(ex, "QRZ pre-lookup for auto-reply failed for {Callsign}", callsign); }
        }

        // Subtract 1 minute so {last-qso} only shows QSOs clearly before the current exchange,
        // not messages from the same session/minute window.
        var text = await ExpandVariablesAsync(_settings.AutoReplyText, callsign, before: triggerTimestamp);
        // Always wait at least 500 ms – same reason as bot replies (node UDP crash on immediate TX).
        var autoReplyDelayMs = Math.Max(500, Math.Clamp(_settings.ReplyDelaySeconds, 0, 30) * 1000);
        await Task.Delay(autoReplyDelayMs);
        _logger.LogInformation("Auto-reply to new contact {Callsign} via node {NodeId}", callsign, nodeId);
        await SendMessageAsync(callsign, text, sourceNodeId: nodeId);
    }

    private async Task HandleBotCommandAsync(MeshcomMessage message)
    {
        try
        {
            _logger.LogDebug("Bot command received from {From}: {Text}", message.From, message.Text);
            if (!_settings.BotEnabled)
            {
                // ping and version always respond regardless of BotEnabled (diagnostic commands).
                var raw = message.Text?.Trim() ?? string.Empty;
                var cmdName = raw.Equals("ping", StringComparison.OrdinalIgnoreCase)
                    ? "ping"
                    : raw.TrimStart('-').TrimStart('—').Split(' ', 2)[0].ToLowerInvariant();
                if (cmdName is not ("ping" or "version" or "help"))
                {
                    _logger.LogDebug("Bot is disabled – ignoring command from {From}", message.From);
                    return;
                }
            }

            var reply = await _botCommandService.ExecuteAsync(message.Text!, message.From, message);
            reply = await ExpandVariablesAsync(reply, message.From, before: message.Timestamp);

            if (string.IsNullOrWhiteSpace(reply))
            {
                _logger.LogWarning(
                    "Bot: reply is empty after variable expansion for '{Cmd}' from {From}. " +
                    "If using {{telemetry}}, verify TelemetryFilePath is set and writable.",
                    message.Text, message.From);
                reply = _settings.Language == "en" ? "(no data)" : "(keine Daten)";
            }

            var parts = SplitMessage(reply);
            _logger.LogInformation("Bot reply to {From} via node {NodeId} ({Parts} part(s)): {Preview}",
                message.From, message.NodeId, parts.Count, reply.Length > 80 ? reply[..80] + "…" : reply);

            // Always wait at least 500 ms so the node's ACK/echo UDP packets clear before
            // we send our reply – sending immediately after reception crashes the node UDP stack.
            var botDelayMs = Math.Max(500, Math.Clamp(_settings.ReplyDelaySeconds, 0, 30) * 1000);
            await Task.Delay(botDelayMs);

            for (var i = 0; i < parts.Count; i++)
            {
                // Reply via the same node that received the command (message.NodeId is the
                // own hardware node that forwarded the LoRa packet to WebDesk via UDP).
                await SendMessageAsync(message.From, parts[i], sourceNodeId: message.NodeId);
                if (i < parts.Count - 1)
                    await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling bot command from {From}: {Text}", message.From, message.Text);
        }
    }

    // Message splitting and JSON byte validation are handled by MessageValidator (Helpers/MessageValidator.cs).
    // The 'using static' import at the top makes SplitMessage() and JsonEncodedLength() available directly.

    /// <summary>
    /// Substitutes all supported template variables in <paramref name="template"/>.
    /// When <paramref name="callsign"/> is provided, caller-specific variables are resolved.
    /// Variables with no value available are replaced with an empty string.
    /// </summary>
    public string ExpandVariables(string template, string? callsign = null)
    {
        var now     = DateTime.Now;
        var station = callsign != null
            ? _chatService.MhList.FirstOrDefault(s =>
                string.Equals(s.Callsign, callsign, StringComparison.OrdinalIgnoreCase))
            : null;

        QrzInfo? qrz = null;
        if (callsign != null && _settings.Qrz.Enabled)
            _qrzService.TryGetCached(callsign, out qrz);

        var route = station?.LastRelayPath != null
            ? string.Join(" \u2192 ", station.LastRelayPath.Split(',').Select(h => h.Trim()))
            : string.Empty;

        // For stations loaded from an older persisted snapshot (before LastSrcType was introduced),
        // LastSrcType may be null. Fall back to "lora" when the station is known but the type is missing.
        var srcType  = station != null
            ? (station.LastSrcType ?? "lora")
            : string.Empty;
        var srcLabel = srcType.ToLowerInvariant() switch
        {
            "lora" => "LoRa",
            "udp"  => "UDP/Gateway",
            "node" => "Node",
            _      => srcType
        };

        var locator = (station?.Latitude.HasValue == true && station.Longitude.HasValue)
            ? Helpers.GeoHelper.ToMaidenhead(station.Latitude.Value, station.Longitude.Value)
            : string.Empty;

        var myLocator = (Status.OwnLatitude.HasValue && Status.OwnLongitude.HasValue)
            ? Helpers.GeoHelper.ToMaidenhead(Status.OwnLatitude.Value, Status.OwnLongitude.Value)
            : string.Empty;

        var cableLossPerM = _settings.CableType switch
        {
            "rg174"    => 5.00 / 10.0,
            "rg58"     => 2.50 / 10.0,
            "rg8x"     => 3.00 / 10.0,
            "rg8"      => 1.80 / 10.0,
            "rg213"    => 1.20 / 10.0,
            "h155"     => 0.80 / 10.0,
            "lmr200"   => 1.00 / 10.0,
            "lmr240"   => 0.75 / 10.0,
            "aircell7" => 0.60 / 10.0,
            "lmr400"   => 0.40 / 10.0,
            "cfd400"   => 0.45 / 10.0,
            "ecoflex10"=> 0.55 / 10.0,
            "ecoflex15"=> 0.37 / 10.0,
            "h2000flex"=> 0.33 / 10.0,
            "lmr600"   => 0.25 / 10.0,
            "custom"   => _settings.CustomCableLossDbPer10m / 10.0,
            _          => 0.0,
        };
        var myEirp = _settings.TxPowerDbm - cableLossPerM * _settings.CableLengthM + _settings.AntennaGainDbi;

        var telemetry = template.Contains("{telemetry}", StringComparison.OrdinalIgnoreCase)
            ? BuildTelemetryString(_settings, out _, out _, out _) ?? string.Empty
            : string.Empty;

        return template
            .Replace("{telemetry}",     telemetry,                                          StringComparison.OrdinalIgnoreCase)
            .Replace("{version}",       AppVersion,                                         StringComparison.OrdinalIgnoreCase)
            .Replace("{mycall}",        _settings.MyCallsign,                               StringComparison.OrdinalIgnoreCase)
            .Replace("{mylocator}",     myLocator,                                          StringComparison.OrdinalIgnoreCase)
            .Replace("{node-firmware}", Status.NodeFirmware             ?? string.Empty,    StringComparison.OrdinalIgnoreCase)
            .Replace("{node-hw}",       MeshcomLookup.HwName(Status.NodeHwId),              StringComparison.OrdinalIgnoreCase)
            .Replace("{callsign}",      callsign              ?? string.Empty,              StringComparison.OrdinalIgnoreCase)
            .Replace("{dest-name}",     qrz?.FirstName        ?? string.Empty,              StringComparison.OrdinalIgnoreCase)
            .Replace("{dest-loc}",      qrz?.Location         ?? string.Empty,              StringComparison.OrdinalIgnoreCase)
            .Replace("{locator}",       locator,                                            StringComparison.OrdinalIgnoreCase)
            .Replace("{rssi}",          station?.LastRssi?.ToString()      ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{snr}",           station?.LastSnr?.ToString("F1")   ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{hw}",            MeshcomLookup.HwName(station?.HwId),                StringComparison.OrdinalIgnoreCase)
            .Replace("{route}",         route,                                              StringComparison.OrdinalIgnoreCase)
            .Replace("{hops}",          station?.HopCount.ToString()       ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{srctype-label}", srcLabel,                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{srctype}",       srcType,                                            StringComparison.OrdinalIgnoreCase)
            .Replace("{date}",             now.ToString("dd.MM.yyyy"),                         StringComparison.OrdinalIgnoreCase)
            .Replace("{time}",             now.ToString("HH:mm"),                              StringComparison.OrdinalIgnoreCase)
            .Replace("{my-tx-power}",      $"{_settings.TxPowerDbm} dBm",                     StringComparison.OrdinalIgnoreCase)
            .Replace("{my-eirp}",          $"{myEirp:F2} dBm",                                StringComparison.OrdinalIgnoreCase)
            .Replace("{my-antenna}",       $"{_settings.AntennaGainDbi} dBi",                 StringComparison.OrdinalIgnoreCase)
            .Replace("{my-antenna-type}",  _settings.AntennaType,                             StringComparison.OrdinalIgnoreCase)
            .Replace("{my-antenna-height}",$"{_settings.AntennaHeightM} m",                   StringComparison.OrdinalIgnoreCase)
            .Replace("{my-freq}",          $"{_settings.FrequencyMhz} MHz",                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Async variant of <see cref="ExpandVariables"/> that additionally resolves <c>{last-qso}</c>
    /// by querying the MySQL database for the last direct message timestamp with <paramref name="callsign"/>.
    /// Falls back to in-memory messages when the DB is not available.
    /// </summary>
    public async Task<string> ExpandVariablesAsync(string template, string? callsign = null, DateTime? before = null, CancellationToken ct = default)
    {
        // First apply all synchronous variables
        var result = ExpandVariables(template, callsign);

        // Resolve {last-qso} only when needed
        if (!result.Contains("{last-qso}", StringComparison.OrdinalIgnoreCase))
            return result;

        string lastQsoStr = string.Empty;

        if (callsign != null)
        {
            // 1. Try MySQL DB (full history)
            var callsignBase = callsign.Contains('-') ? callsign[..callsign.IndexOf('-')] : callsign;
            var dbTime = await _qsoSummaryService.GetLastQsoTimeDbOnlyAsync(callsignBase, before, sessionGapMinutes: 10, ct);
            if (dbTime.HasValue)
            {
                lastQsoStr = dbTime.Value.ToString("dd.MM.yyyy HH:mm");
            }
            else
            {
                // 2. Fallback: find last message before a session gap of >= 10 min in memory
                var msgs = _chatService.GetTabMessages(callsign)
                    .Where(m => before == null || m.Timestamp < before.Value)
                    .OrderByDescending(m => m.Timestamp)
                    .ToList();
                for (int i = 0; i < msgs.Count - 1; i++)
                {
                    if ((msgs[i].Timestamp - msgs[i + 1].Timestamp).TotalMinutes >= 10)
                    {
                        lastQsoStr = msgs[i + 1].Timestamp.ToString("dd.MM.yyyy HH:mm");
                        break;
                    }
                }
            }
        }

        result = result.Replace("{last-qso}",
            string.IsNullOrEmpty(lastQsoStr) ? "kein QSO" : lastQsoStr,
            StringComparison.OrdinalIgnoreCase);

        return result;
    }

    /// <summary>
    /// Checks beacon settings every minute. Sends a beacon to <see cref="MeshcomSettings.BeaconGroup"/>
    /// when <see cref="MeshcomSettings.BeaconEnabled"/> is true and the configured interval has elapsed.
    /// All settings are read from the live <see cref="_settings"/> field so changes apply without restart.
    /// </summary>
    private async Task RunBeaconAsync(CancellationToken stoppingToken)
    {
        var lastSent = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            var s = _settings;
            bool configured = s.BeaconEnabled
                && !string.IsNullOrWhiteSpace(s.BeaconGroup)
                && !string.IsNullOrWhiteSpace(s.BeaconText);

            if (!configured)
            {
                SetBeaconStatus(false, null);
                continue;
            }

            var interval = TimeSpan.FromHours(Math.Max(1, s.BeaconIntervalHours));

            // On first activation, record the current time so the first beacon fires
            // after one full interval – not immediately on startup or when enabling.
            if (lastSent == DateTime.MinValue)
                lastSent = DateTime.Now;

            var nextDue = lastSent + interval;

            SetBeaconStatus(true, nextDue > DateTime.Now ? nextDue : DateTime.Now);

            if (DateTime.Now < nextDue)
                continue;

            var destination = ResolveDestination(s.BeaconGroup);
            var beaconTabKey = ResolveTabKey(s.BeaconGroup);
            var beaconText  = ExpandVariables(s.BeaconText);
            var parts       = SplitMessage(beaconText);
            _logger.LogInformation("Sending beacon to {Group} ({Parts} part(s))", s.BeaconGroup, parts.Count);
            for (var i = 0; i < parts.Count; i++)
            {
                await SendMessageAsync(destination, parts[i], beaconTabKey);
                if (i < parts.Count - 1)
                    await Task.Delay(TimeSpan.FromSeconds(2));
            }
            lastSent = DateTime.Now;
            SetBeaconStatus(true, lastSent + interval);
        }

        SetBeaconStatus(false, null);
    }

    private void SetBeaconStatus(bool active, DateTime? nextSend)
    {
        if (Status.BeaconActive == active && Status.BeaconNextSend == nextSend) return;
        Status.BeaconActive  = active;
        Status.BeaconNextSend = nextSend;
        NotifyStatusChange();
    }

    /// <summary>
    /// Checks telemetry settings every minute. Reads the configured JSON file and sends a
    /// formatted telemetry message to <see cref="MeshcomSettings.TelemetryGroup"/> when
    /// <see cref="MeshcomSettings.TelemetryEnabled"/> is true and the interval has elapsed.
    /// </summary>
    private async Task RunTelemetryAsync(CancellationToken stoppingToken)
    {
        // On startup, pre-fill lastSentSlot with the current slot so that a restart
        // during a scheduled hour does not immediately re-send the telemetry message.
        var startupNow   = DateTime.Now;
        var lastSentSlot = (Date: DateOnly.FromDateTime(startupNow), Hour: startupNow.Hour);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            var s = _settings;

            // Extudp runs on its own send-on-change schedule, independent of the hour-slot
            // schedule below (which only applies to the text-message path). The node applies
            // no rate limiting of its own for externally pushed telemetry, so this is the only
            // guard against flooding the mesh / the node's shared TX ring buffer.
            await CheckAndSendExtUdpIfChangedAsync(s);

            bool configured = s.TelemetryEnabled
                && !string.IsNullOrWhiteSpace(s.TelemetryFilePath)
                && !string.IsNullOrWhiteSpace(s.TelemetryGroup)
                && s.TelemetryMapping.Count > 0;

            if (!configured)
            {
                SetTelemetryStatus(false, null);
                continue;
            }

            var scheduleHours = ParseScheduleHours(s.TelemetryScheduleHours);
            if (scheduleHours.Count == 0)
            {
                SetTelemetryStatus(false, null);
                continue;
            }

            var now         = DateTime.Now;
            var currentSlot = (DateOnly.FromDateTime(now), now.Hour);

            SetTelemetryStatus(true, ComputeNextScheduledTime(scheduleHours, now));

            if (scheduleHours.Contains(now.Hour) && lastSentSlot != currentSlot)
            {
                await SendTextTelemetryAsync(s);
                lastSentSlot = currentSlot;
                SetTelemetryStatus(true, ComputeNextScheduledTime(scheduleHours, DateTime.Now));
            }
        }

        SetTelemetryStatus(false, null);
    }

    private static HashSet<int> ParseScheduleHours(string input)
    {
        if (input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Any(p => p == "*"))
            return Enumerable.Range(0, 24).ToHashSet();

        return input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => int.TryParse(p, out var h) && h >= 0 && h <= 23 ? h : -1)
                    .Where(h => h >= 0)
                    .ToHashSet();
    }

    private static DateTime ComputeNextScheduledTime(HashSet<int> hours, DateTime from)
    {
        var sorted   = hours.OrderBy(h => h).ToList();
        var nextHour = sorted.FirstOrDefault(h => h > from.Hour, -1);
        return nextHour >= 0
            ? from.Date.AddHours(nextHour)
            : from.Date.AddDays(1).AddHours(sorted[0]);
    }

    /// <summary>
    /// Immediately reads the telemetry file and sends all configured values.
    /// Used by the Settings UI for manual test sends without waiting for the interval.
    /// </summary>
    public Task SendTelemetryNowAsync() => SendTelemetryAsync(_settings);

    /// <summary>
    /// Immediately sends the configured beacon to <see cref="MeshcomSettings.BeaconGroup"/>.
    /// Used by the Settings UI to test the beacon without waiting for the interval.
    /// </summary>
    public async Task SendBeaconNowAsync()
    {
        var s = _settings;
        if (string.IsNullOrWhiteSpace(s.BeaconGroup))
            throw new InvalidOperationException("Keine Baken-Gruppe konfiguriert.");
        if (string.IsNullOrWhiteSpace(s.BeaconText))
            throw new InvalidOperationException("Kein Bakentext konfiguriert.");

        var destination = ResolveDestination(s.BeaconGroup);
        var beaconTabKey = ResolveTabKey(s.BeaconGroup);
        var beaconText  = ExpandVariables(s.BeaconText);
        var parts       = SplitMessage(beaconText);
        _logger.LogInformation("Beacon test send to {Group} ({Parts} part(s)): {Text}", s.BeaconGroup, parts.Count, beaconText);
        for (var i = 0; i < parts.Count; i++)
        {
            await SendMessageAsync(destination, parts[i], beaconTabKey);
            if (i < parts.Count - 1)
                await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Immediately sends the configured auto-reply text to <paramref name="callsign"/>.
    /// Used by the Settings UI to test the auto-reply without waiting for an incoming message.
    /// </summary>
    public Task SendAutoReplyNowAsync(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            throw new ArgumentException("Kein Rufzeichen angegeben.");
        if (string.IsNullOrWhiteSpace(_settings.AutoReplyText))
            throw new InvalidOperationException("Kein Auto-Reply Text konfiguriert.");

        var text = ExpandVariables(_settings.AutoReplyText, callsign);
        _logger.LogInformation("Auto-reply test send to {Callsign}: {Text}", callsign, text);
        return SendMessageAsync(callsign, text);
    }

    /// <summary>
    /// Parses a telemetry JSON value that may be either a JSON number or a numeric string.
    /// Shared by the text-message and extudp telemetry builders.
    /// </summary>
    private static bool TryParseTelemetryValue(JsonElement valueProp, out double value)
    {
        if (valueProp.ValueKind == JsonValueKind.Number)
        {
            value = valueProp.GetDouble();
            return true;
        }
        if (valueProp.ValueKind == JsonValueKind.String &&
            double.TryParse(valueProp.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>
    /// True when <paramref name="hpa"/> is a physically plausible surface air pressure.
    /// Guards the extudp telemetry parser against non-pressure values that arrive under
    /// the <c>qfe</c>/<c>qnh</c> keys – e.g. a heard station's <c>qfe</c> actually carries
    /// the APRS <c>/F=</c> field, which the firmware fills with the barometric altitude in
    /// metres (~150–250), not a pressure. Range covers the WMO record extremes with margin.
    /// </summary>
    private static bool IsPlausiblePressure(double hpa) => hpa is >= 540 and <= 1080;

    /// <summary>
    /// Reads the telemetry JSON file and builds the flat "key=value unit" string
    /// from the configured <see cref="MeshcomSettings.TelemetryMapping"/>.
    /// Returns <c>null</c> when the file does not exist, cannot be parsed, or yields no values.
    /// Also returns captured weather roles via out parameters so the caller can update
    /// <see cref="ServiceStatus"/> without re-reading the file.
    /// </summary>
    private string? BuildTelemetryString(MeshcomSettings s,
        out double? ownTemp, out double? ownHumidity, out double? ownPressure)
    {
        ownTemp = null; ownHumidity = null; ownPressure = null;

        try
        {
            if (string.IsNullOrWhiteSpace(s.TelemetryFilePath) || !File.Exists(s.TelemetryFilePath))
                return null;

            var fileContent = File.ReadAllText(s.TelemetryFilePath);
            using var doc = JsonDocument.Parse(fileContent);
            var root  = doc.RootElement;

            // The Weather API writes an _observed timestamp into the file. When provider
            // fetches keep failing, the file stays untouched – without this check the old
            // values would be rebroadcast as current indefinitely.
            if (s.WeatherApi.Provider != WeatherProvider.None &&
                root.TryGetProperty("_observed", out var observedProp) &&
                observedProp.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(observedProp.GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal |
                    System.Globalization.DateTimeStyles.AdjustToUniversal, out var observedUtc))
            {
                var maxAge = TimeSpan.FromMinutes(Math.Max(60, s.WeatherApi.PollIntervalMinutes * 3));
                var age    = DateTime.UtcNow - observedUtc;
                if (age > maxAge)
                {
                    _logger.LogWarning(
                        "Telemetry file {Path} is stale (observed {Observed:u}, age {Age:F0} min > max {Max:F0} min) – not sending",
                        s.TelemetryFilePath, observedUtc, age.TotalMinutes, maxAge.TotalMinutes);
                    return null;
                }
            }

            var parts = new List<string>();

            foreach (var entry in s.TelemetryMapping)
            {
                if (string.IsNullOrWhiteSpace(entry.JsonKey)) continue;
                if (!root.TryGetProperty(entry.JsonKey, out var valueProp)) continue;
                if (!TryParseTelemetryValue(valueProp, out var value)) continue;

                // Capture well-known weather values for map popup.
                // Explicit WeatherRole takes precedence; unit-based detection is the fallback
                // for entries that were configured before the Role field was introduced.
                var role = entry.WeatherRole.Trim().ToLowerInvariant();
                if (role == string.Empty)
                {
                    var unitNorm = entry.Unit.Trim().TrimStart('°').ToLowerInvariant();
                    if      (unitNorm is "c")                      role = "temp";
                    else if (unitNorm is "%")                      role = "humidity";
                    else if (unitNorm is "hpa" or "mbar" or "mb")  role = "pressure";
                }
                if      (role == "temp")     ownTemp     ??= value;
                else if (role == "humidity") ownHumidity ??= value;
                else if (role == "pressure") ownPressure ??= value;

                var decimals  = Math.Max(0, entry.Decimals);
                var formatted = value.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
                var label     = string.IsNullOrWhiteSpace(entry.Label) ? entry.JsonKey : entry.Label;
                parts.Add($"{label}={formatted}{entry.Unit}");
            }

            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Forces an immediate send on both telemetry paths: the classic text-message path (when
    /// <see cref="MeshcomSettings.TelemetryGroup"/> is set) and the native extudp "tele"
    /// telegram (when configured), bypassing the extudp send-on-change throttle. Used only by
    /// <see cref="SendTelemetryNowAsync"/> (the Settings UI's manual test-send button) — the
    /// scheduled hour-slot loop in <see cref="RunTelemetryAsync"/> calls
    /// <see cref="SendTextTelemetryAsync"/> and <see cref="CheckAndSendExtUdpIfChangedAsync"/>
    /// directly and independently instead.
    /// </summary>
    private async Task SendTelemetryAsync(MeshcomSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.TelemetryGroup))
            await SendTextTelemetryAsync(s);
        else
            _logger.LogInformation("Telemetry test send: TelemetryGroup is empty – skipping text-message path.");

        if (s.TelemetryExtUdpEnabled && s.TelemetryMapping.Any(e => RoleToExtUdpField.ContainsKey(e.WeatherRole.Trim().ToLowerInvariant())))
            await SendExtUdpTeleAsync(s);
        else if (s.TelemetryExtUdpEnabled)
            _logger.LogInformation("Telemetry test send: extudp enabled but no mapping entry has an assigned role (temp/humidity/pressure/temp2/qnh/gasres/co2) – skipping extudp path.");
        else
            _logger.LogInformation("Telemetry test send: extudp is disabled – skipping extudp path.");
    }

    private async Task SendTextTelemetryAsync(MeshcomSettings s)
    {
        try
        {
            var telemetryString = BuildTelemetryString(s, out var ownTemp, out var ownHumidity, out var ownPressure);

            if (telemetryString is null)
            {
                if (!File.Exists(s.TelemetryFilePath))
                    _logger.LogWarning("Telemetry file not found: {Path}", s.TelemetryFilePath);
                else
                    _logger.LogWarning("No telemetry values could be read from {Path}", s.TelemetryFilePath);
                return;
            }

            var parts = telemetryString.Split(' ').ToList();

            // Build prefix from own GPS position (Maidenhead locator) or fall back to "TM"
            var locator = (Status.OwnLatitude.HasValue && Status.OwnLongitude.HasValue)
                ? Helpers.GeoHelper.ToMaidenhead(Status.OwnLatitude.Value, Status.OwnLongitude.Value)
                : null;

            // Pack parts into 150-char buckets.
            // Reserve up to 11 chars for the longest prefix ("JN48QN/99:") + 1 space.
            const int MaxMsg    = 150;
            const int PrefixMax = 11;
            const int BucketLen = MaxMsg - PrefixMax;

            var buckets = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var part in parts)
            {
                var sep = current.Length == 0 ? string.Empty : " ";
                if (current.Length + sep.Length + part.Length > BucketLen)
                {
                    if (current.Length > 0)
                        buckets.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(part);
            }
            if (current.Length > 0)
                buckets.Add(current.ToString());

            var destination = s.TelemetryGroup.TrimStart('#');

            for (int i = 0; i < buckets.Count; i++)
            {
                // Single bucket  → "JN48QN:"  or "TM:"
                // Multiple buckets → "JN48QN/1:" or "TM1:"
                string prefix = buckets.Count == 1
                    ? (locator ?? "TM") + ":"
                    : (locator ?? "TM") + $"/{i + 1}:";
                var msg    = $"{prefix} {buckets[i]}";

                _logger.LogInformation(
                    "Sending telemetry [{Index}/{Total}] to {Group}: {Msg}",
                    i + 1, buckets.Count, s.TelemetryGroup, msg);

                await SendMessageAsync(destination, msg, s.TelemetryGroup);

                // Brief pause between consecutive packets to avoid node flooding
                if (i < buckets.Count - 1)
                    await Task.Delay(TimeSpan.FromSeconds(2));
            }

            // Persist captured weather values in Status so the map popup can show them
            if (ownTemp.HasValue || ownHumidity.HasValue || ownPressure.HasValue)
            {
                Status.OwnTemp             = ownTemp;
                Status.OwnHumidity         = ownHumidity;
                Status.OwnPressure         = ownPressure;
                Status.OwnTelemetrySentTime = DateTime.UtcNow;
                NotifyStatusChange();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading or sending telemetry from {Path}", s.TelemetryFilePath);
        }
    }

    /// <summary>
    /// Reads the telemetry JSON file and resolves the mapping entries whose
    /// <see cref="TelemetryMappingEntry.WeatherRole"/> is one of the 7 fixed extudp fields
    /// (<see cref="RoleToExtUdpField"/>). When two entries share the same role, the first one
    /// (in <see cref="MeshcomSettings.TelemetryMapping"/> order) wins. Returns <c>null</c> when
    /// the file is missing, unparsable, or no role-assigned value could be resolved.
    /// </summary>
    private Dictionary<string, double>? BuildExtUdpValues(MeshcomSettings s)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(s.TelemetryFilePath) || !File.Exists(s.TelemetryFilePath))
                return null;

            var fileContent = File.ReadAllText(s.TelemetryFilePath);
            using var doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

            var resolved = new Dictionary<string, double>();

            foreach (var entry in s.TelemetryMapping)
            {
                if (string.IsNullOrWhiteSpace(entry.JsonKey)) continue;
                if (!RoleToExtUdpField.TryGetValue(entry.WeatherRole.Trim().ToLowerInvariant(), out var field)) continue;
                if (resolved.ContainsKey(field)) continue; // first mapping entry for this role wins
                if (!root.TryGetProperty(entry.JsonKey, out var valueProp)) continue;
                if (!TryParseTelemetryValue(valueProp, out var value)) continue;

                resolved[field] = value;
            }

            return resolved.Count > 0 ? resolved : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sends the values assigned to a <see cref="TelemetryMappingEntry.WeatherRole"/> as a
    /// native <c>{"type":"tele","temp":...,"hum":...,...}</c> UDP telegram directly to the node
    /// (same endpoint as <see cref="SendMessageAsync"/>). The node writes each field straight
    /// into its own sensor variables and immediately re-beacons its position, so the values
    /// appear embedded in the position comment exactly like a real onboard sensor would.
    /// </summary>
    private async Task SendExtUdpTeleAsync(MeshcomSettings s)
    {
        if (_udpClient == null)
        {
            _logger.LogWarning("Cannot send extudp telemetry – UDP client not initialized");
            return;
        }

        try
        {
            var values = BuildExtUdpValues(s);
            if (values is null)
            {
                if (!File.Exists(s.TelemetryFilePath))
                    _logger.LogWarning("Extudp telemetry: telemetry file not found: {Path}", s.TelemetryFilePath);
                else
                    _logger.LogWarning("Extudp telemetry: no role-assigned values could be read from {Path}", s.TelemetryFilePath);
                return;
            }

            var payload = new Dictionary<string, object> { ["type"] = "tele" };
            foreach (var (field, value) in values)
                payload[field] = value;

            var json  = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);

            if (bytes.Length > 254)
            {
                _logger.LogWarning(
                    "Extudp telemetry payload too large ({Bytes} bytes, max 254) – Senden abgebrochen", bytes.Length);
                return;
            }

            var sendingNode = _nodeManager.SelectedNode;
            var deviceIp    = sendingNode?.DeviceIp   ?? s.DeviceIp;
            var devicePort  = sendingNode?.DevicePort ?? s.DevicePort;
            var remoteEp    = new IPEndPoint(IPAddress.Parse(deviceIp), devicePort);

            await _udpClient.SendAsync(bytes, bytes.Length, remoteEp);
            _logger.LogInformation("Sending native extudp telemetry to {Remote}: {Data}", remoteEp, json);
            if (s.LogUdpTraffic)
                _logger.LogInformation("[UDP-TX] {Remote} {Data}", remoteEp, json);

            Status.TxCount++;
            Status.LastTxTime = DateTime.Now;
            NotifyStatusChange();

            // Record what was actually sent, for the send-on-change throttle in
            // CheckAndSendExtUdpIfChangedAsync. Updated here (the single choke point for all
            // extudp sends, automatic or manual "send now") so the throttle timer always
            // reflects real airtime usage.
            _lastExtUdpSentValues = values.Select(kv => (kv.Key, kv.Value)).ToList();
            _lastExtUdpSentTime   = DateTime.UtcNow;

            // Arm the firmware-capability probe (see EvaluateExtUdpProbe / the timeout sweep in
            // CheckAndSendExtUdpIfChangedAsync) with only the fields WebDesk can read back out of
            // a telemetry echo unambiguously.
            var probeFields = values.Where(kv => kv.Key is "temp" or "hum" or "temp2")
                                     .Select(kv => (kv.Key, kv.Value)).ToList();
            if (probeFields.Count > 0)
            {
                _pendingExtUdpProbe       = probeFields;
                _pendingExtUdpProbeSentAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending extudp telemetry telegram");
        }
    }

    /// <summary>
    /// Send-on-change check for the extudp telemetry path, called once per minute from
    /// <see cref="RunTelemetryAsync"/>. The node applies no rate limiting of its own for
    /// externally pushed telemetry (every accepted packet is forwarded via LoRa immediately),
    /// so this throttle is the only guard against flooding the mesh / the node's shared TX ring
    /// buffer when a source value jitters. A send only happens when at least one value differs
    /// from the last successfully sent set AND the configured minimum interval has elapsed;
    /// unchanged values never trigger a send regardless of the interval.
    /// </summary>
    private async Task CheckAndSendExtUdpIfChangedAsync(MeshcomSettings s)
    {
        // Firmware-capability check: if the node never echoed back the values from the last
        // probe within the timeout, it does not implement the extudp "tele" interface (or has
        // real sensor hardware installed, which behaves identically from here) – stop sending.
        if (_pendingExtUdpProbe is not null && DateTime.UtcNow - _pendingExtUdpProbeSentAt > ExtUdpProbeTimeout)
        {
            _pendingExtUdpProbe = null;
            await DisableExtUdpTelemetryAsync(
                "the node did not echo back matching values within the expected time – its firmware likely does not implement the extudp \"tele\" interface (or has real sensor hardware installed)");
            return;
        }

        if (!s.TelemetryEnabled || !s.TelemetryExtUdpEnabled) return;
        if (!s.TelemetryMapping.Any(e => RoleToExtUdpField.ContainsKey(e.WeatherRole.Trim().ToLowerInvariant()))) return;

        var current = BuildExtUdpValues(s);
        if (current is null)
        {
            // Log once when this condition starts, not on every per-minute tick it persists.
            if (!_lastExtUdpBuildFailed)
            {
                if (!File.Exists(s.TelemetryFilePath))
                    _logger.LogWarning("Extudp telemetry: telemetry file not found: {Path}", s.TelemetryFilePath);
                else
                    _logger.LogWarning("Extudp telemetry: no role-assigned values could be read from {Path}", s.TelemetryFilePath);
                _lastExtUdpBuildFailed = true;
            }
            return;
        }
        if (_lastExtUdpBuildFailed)
        {
            _logger.LogInformation("Extudp telemetry: values available again from {Path}", s.TelemetryFilePath);
            _lastExtUdpBuildFailed = false;
        }

        var currentKeyed = current.Select(kv => (kv.Key, kv.Value)).OrderBy(kv => kv.Key).ToList();
        if (_lastExtUdpSentValues is not null && currentKeyed.SequenceEqual(_lastExtUdpSentValues.OrderBy(kv => kv.Field)))
            return;

        var minInterval = TimeSpan.FromMinutes(Math.Max(1, s.TelemetryExtUdpMinIntervalMinutes));
        if (_lastExtUdpSentTime is not null && DateTime.UtcNow - _lastExtUdpSentTime.Value < minInterval)
            return; // changed, but throttled – retried on the next tick as long as the change persists

        await SendExtUdpTeleAsync(s);
    }

    /// <summary>
    /// Compares an incoming own-node telemetry echo against <see cref="_pendingExtUdpProbe"/>
    /// (armed by <see cref="SendExtUdpTeleAsync"/>). Confirms extudp telemetry support the first
    /// time a matching echo arrives; stale/unrelated echoes (e.g. from a periodic beacon that
    /// happens to arrive while a probe is pending) are ignored rather than treated as a mismatch,
    /// so only a full timeout (handled in <see cref="CheckAndSendExtUdpIfChangedAsync"/>) counts
    /// as "not supported".
    /// </summary>
    private void EvaluateExtUdpProbe(MeshcomMessage message)
    {
        if (_pendingExtUdpProbe is null) return;
        if (DateTime.UtcNow - _pendingExtUdpProbeSentAt > ExtUdpProbeTimeout)
        {
            _pendingExtUdpProbe = null; // timeout sweep in CheckAndSendExtUdpIfChangedAsync handles disabling
            return;
        }

        const double tolerance = 0.1;
        bool allMatch = _pendingExtUdpProbe.All(p => p.Field switch
        {
            "temp"  => message.Temp1.HasValue    && Math.Abs(message.Temp1.Value    - p.Value) < tolerance,
            "hum"   => message.Humidity.HasValue && Math.Abs(message.Humidity.Value - p.Value) < tolerance,
            "temp2" => message.Temp2.HasValue    && Math.Abs(message.Temp2.Value    - p.Value) < tolerance,
            _       => true
        });
        if (!allMatch) return; // not our echo (yet) – keep waiting until it matches or times out

        _pendingExtUdpProbe = null;
        if (Status.ExtUdpTelemetryConfirmed != true)
        {
            Status.ExtUdpTelemetryConfirmed = true;
            _logger.LogInformation("Extudp telemetry confirmed: the node echoed back the values we sent.");
            NotifyStatusChange();
        }
    }

    /// <summary>
    /// Turns off <see cref="MeshcomSettings.TelemetryExtUdpEnabled"/> and persists it, so the app
    /// stops wasting mesh airtime on a node whose firmware does not apply the values (see
    /// <see cref="CheckAndSendExtUdpIfChangedAsync"/>'s timeout sweep). The user can re-enable it
    /// manually in Settings at any time (e.g. after flashing supporting firmware), which re-arms
    /// the probe on the next send.
    /// </summary>
    private async Task DisableExtUdpTelemetryAsync(string reason)
    {
        if (!_settings.TelemetryExtUdpEnabled) return; // already off

        _logger.LogWarning(
            "Disabling extudp telemetry automatically: {Reason}.", reason);
        Status.ExtUdpTelemetryConfirmed = false;
        NotifyStatusChange();

        _settings.TelemetryExtUdpEnabled = false;
        try
        {
            await _settingsService.SaveMeshcomSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist automatic extudp telemetry disable");
        }
    }

    private void SetTelemetryStatus(bool active, DateTime? nextSend)
    {
        if (Status.TelemetryActive == active && Status.TelemetryNextSend == nextSend) return;
        Status.TelemetryActive  = active;
        Status.TelemetryNextSend = nextSend;
        NotifyStatusChange();
    }

    /// <summary>
    /// Send a text message to the MeshCom device via UDP.
    /// </summary>
    /// <param name="destination">Wire destination sent to the node (no leading '#', e.g. "9" for group #9).</param>
    /// <param name="text">Message text.</param>
    /// <param name="tabKey">Original chat-tab key (e.g. "#9", "*"). Used for local tab routing only.
    /// When null, <paramref name="destination"/> is used for both wire and tab routing.</param>
    /// <param name="sourceNodeId">When set, overrides the selected node and sends via this node instead.
    /// Used by AutoReply and Bot to reply on the same node that received the incoming message.</param>
    public async Task SendMessageAsync(string destination, string text, string? tabKey = null, Guid? sourceNodeId = null)
    {
        if (_udpClient == null)
        {
            _logger.LogWarning("Cannot send – UDP client not initialized");
            return;
        }

        if (text.Length > 149)
        {
            _logger.LogWarning(
                "Nachricht zu lang ({Length} Zeichen, max 149) – Senden abgebrochen: \"{Excerpt}\"",
                text.Length, text[..Math.Min(60, text.Length)]);
            return;
        }

        try
        {
            // Resolve sending node:
            // 1. sourceNodeId (AutoReply/Bot – same node that received the message)
            // 2. SelectedNode (manual send from UI)
            // 3. Legacy fallback to top-level settings
            var sendingNode = sourceNodeId is not null
                ? _nodeManager.Nodes.FirstOrDefault(n => n.Id == sourceNodeId) ?? _nodeManager.SelectedNode
                : _nodeManager.SelectedNode;
            var selectedNodeId = sendingNode?.Id;
            var fromCallsign   = sendingNode?.Callsign ?? _settings.MyCallsign;
            var deviceIp       = sendingNode?.DeviceIp  ?? _settings.DeviceIp;
            var devicePort     = sendingNode?.DevicePort ?? _settings.DevicePort;

            // Transport weiche: a KISS/TCP node is served by KissClientService, not the UDP socket.
            if (sendingNode?.Transport == NodeTransport.Kiss && _kiss is not null)
            {
                var resolvedKissTabKey = tabKey ?? destination;
                var kissOutgoing = new MeshcomMessage
                {
                    From           = fromCallsign,
                    To             = resolvedKissTabKey,
                    Text           = text,
                    IsOutgoing     = true,
                    RawData        = $"{fromCallsign}>APRS::{destination}:{text}",
                    SequenceNumber = "TX",
                    NodeId         = selectedNodeId,
                };
                _chatService.AddOutgoingMessage(kissOutgoing);
                var sent = await _kiss.SendMessageFrameAsync(sendingNode, destination, text, kissOutgoing);
                if (sent)
                {
                    Status.TxCount++;
                    Status.LastTxTime = DateTime.Now;
                    NotifyStatusChange();
                }
                _chatService.NotifyExternalChange();
                return;
            }

            var json = JsonSerializer.Serialize(new { type = "msg", dst = destination, msg = text });
            var bytes = Encoding.UTF8.GetBytes(json);

            if (bytes.Length > 254)
            {
                _logger.LogWarning(
                    "JSON-Paket zu groß ({Bytes} Bytes, max 254) – Senden abgebrochen: \"{Excerpt}\"",
                    bytes.Length, text[..Math.Min(40, text.Length)]);
                return;
            }

            var remoteEp = new IPEndPoint(IPAddress.Parse(deviceIp), devicePort);

            await _udpClient.SendAsync(bytes, bytes.Length, remoteEp);
            _logger.LogDebug("UDP TX [{Remote}] as {From}: {Data}", remoteEp, fromCallsign, json);
            if (_settings.LogUdpTraffic)
                _logger.LogInformation("[UDP-TX] {Remote} {Data}", remoteEp, json);

            Status.TxCount++;
            Status.LastTxTime = DateTime.Now;
            NotifyStatusChange();

            var resolvedTabKey = tabKey ?? destination;
            var outgoing = new MeshcomMessage
            {
                From           = fromCallsign,
                To             = resolvedTabKey,
                Text           = text,
                IsOutgoing     = true,
                RawData        = json,
                SequenceNumber = "TX",
                NodeId         = selectedNodeId
            };
            _chatService.AddOutgoingMessage(outgoing);

            // Monitor whether the node echoes back the packet within 5 seconds.
            // The node echoes every sent message (direct, group, broadcast) via src_type:"node".
            // Group and broadcast echoes carry no {NNN} sequence number – handled in AssignOutgoingSequence.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (outgoing.NodeEchoReceived == null)
                {
                    outgoing.NodeEchoReceived = false;
                    _logger.LogWarning(
                        "Node-Echo ausgeblieben – UDP-Paket wurde möglicherweise nicht vom Node empfangen. Ziel={Dst} Text=\"{Text}\"",
                        destination, text);
                    _chatService.NotifyEchoTimeout();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending UDP data to {DeviceIp}:{DevicePort}", _settings.DeviceIp, _settings.DevicePort);
        }
    }

    private void NotifyStatusChange() => OnStatusChange?.Invoke();

    /// <summary>
    /// Feeds an RX frame decoded by another transport (currently KISS) into the shared
    /// <see cref="ConnectionStatus"/> so the status bar (RX count, last RX, RSSI/SNR)
    /// keeps working when the primary node is not on ext-udp.
    /// </summary>
    public void RecordTransportRx(MeshcomMessage message)
    {
        if (message.Rssi.HasValue)
        {
            Status.LastRssi = message.Rssi;
            Status.LastSnr  = message.Snr;
        }
        Status.RxCount++;
        Status.LastRxTime = message.Timestamp;
        Status.LastRxFrom = message.From;
        NotifyStatusChange();
    }

    /// <summary>
    /// Updates the own GPS position (called from browser geolocation or node position beacon).
    /// </summary>
    public void SetOwnPosition(double lat, double lon, int? altMeters, string source)
    {
        Status.OwnLatitude = lat;
        Status.OwnLongitude = lon;
        Status.OwnAltitude = altMeters;
        Status.OwnPositionSource = source;
        NotifyStatusChange();
        _logger.LogInformation("Own position updated [{Source}]: {Lat:F5}, {Lon:F5}, {Alt}m",
            source, lat, lon, altMeters);
    }

    /// <summary>
    /// Strips the MeshCom EXTUDP wrapper so the inner JSON can be parsed.
    ///   "[EXT] Out: {JSON} Len: NNN"  →  "{JSON}"
    /// Returns the input unchanged when no wrapper is present.
    /// </summary>
    private static string UnwrapExtMessage(string raw)
    {
        const string prefix = "[EXT] Out: ";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
            return raw;

        var jsonStart = prefix.Length;
        var lenMarker = raw.LastIndexOf(" Len: ", StringComparison.Ordinal);
        return lenMarker > jsonStart ? raw[jsonStart..lenMarker] : raw[jsonStart..];
    }

    private MeshcomMessage? ParseMessage(string raw)
    {
        raw = UnwrapExtMessage(raw);

        if (!raw.StartsWith('{'))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp) ||
                !root.TryGetProperty("src",  out var srcProp))
                return null;

            var msgType          = typeProp.GetString();
            var isPositionBeacon = msgType == "pos";
            var isTelemetry      = msgType == "tele";

            // Handle "msg", "pos", and "tele" types; ignore everything else
            if (msgType != "msg" && msgType != "pos" && msgType != "tele")
                return null;

            var src = srcProp.GetString() ?? string.Empty;

            // "msg" requires dst + msg fields; "pos" may omit them; "tele" has neither
            string dst = "*";
            string msg = string.Empty;
            if (msgType == "msg")
            {
                if (!root.TryGetProperty("dst", out var dstProp) ||
                    !root.TryGetProperty("msg", out var msgProp))
                    return null;
                dst = dstProp.GetString() ?? string.Empty;
                msg = msgProp.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("dst", out var dstProp2))
            {
                dst = dstProp2.GetString() ?? "*";
            }

            // For relayed messages ("OE1XAR-62,DB0TAW-13,..."), use the first callsign as sender;
            // preserve the full path for display.
            var commaIdx  = src.IndexOf(',');
            var sender    = commaIdx >= 0 ? src[..commaIdx] : src;
            var relayPath = commaIdx >= 0 ? src : null;

            // Newer firmware reports via-routing in dst ("DB0FRI-12,DH1FR-2") – the last
            // callsign is the actual destination; preserve the full path for display.
            var dstCommaIdx = dst.IndexOf(',');
            var viaPath     = dstCommaIdx >= 0 ? dst : null;
            if (dstCommaIdx >= 0)
                dst = dst[(dst.LastIndexOf(',') + 1)..].Trim();

            // Extract sequence number from {NNN} before stripping it
            string? seqNum = null;
            if (!isPositionBeacon && !isTelemetry)
            {
                var seqMatch = SequenceNumberPattern().Match(msg);
                if (seqMatch.Success)
                    seqNum = seqMatch.Groups[1].Value;
                msg = TrailingSequencePattern().Replace(msg, string.Empty);
            }

            // Detect ACK/REJ messages: bare APRS "ack187" (standard clients), APRS-style
            // "NOCALL-2 :ack187", or MeshCom inline "NOCALL-2:ack881".
            var isAck = !isPositionBeacon && AckPattern().IsMatch(msg.Trim());

            if (isAck)
            {
                var ackSeqMatch = AckSequencePattern().Match(msg);
                if (ackSeqMatch.Success)
                    seqNum = ackSeqMatch.Groups[1].Value;

                // For MeshCom inline ACKs the DST field from the JSON ("*" or group) is not the
                // real ACK target – the target callsign is encoded inside the msg text itself
                // (e.g. "DL3DCW-12:ack881").  Override dst so MarkMessageAcknowledged can find
                // the correct outgoing message tab. The bare form ("ack881") carries no target,
                // so the JSON dst (the real addressee) is kept.
                var ackTargetMatch = AckTargetPattern().Match(msg.Trim());
                if (ackTargetMatch.Success)
                    dst = ackTargetMatch.Groups[1].Value;
            }

            // Detect MeshCom time-sync broadcasts: "{CET}2026-04-07 18:11:58"
            var isTimeSync = !isAck && !isPositionBeacon && !isTelemetry && TimeSyncPattern().IsMatch(msg);

            // src_type:"node"
            var srcType      = root.TryGetProperty("src_type", out var srcTypeProp) ? (srcTypeProp.GetString() ?? "lora") : "lora";
            var isNodePacket = string.Equals(srcType, "node", StringComparison.OrdinalIgnoreCase);

            int? rssiRaw = (!isNodePacket && root.TryGetProperty("rssi", out var rssiProp)) ? rssiProp.GetInt32() : null;
            int?    rssi = rssiRaw is < 0 ? rssiRaw : null;   // 0 or positive = invalid/unset firmware default
            double? snr  = (!isNodePacket && root.TryGetProperty("snr",  out var snrProp))  ? snrProp.GetDouble() : null;

            // ── msg_id ───────────────────────────────────────────────────────
            string? msgId = root.TryGetProperty("msg_id", out var msgIdProp) ? msgIdProp.GetString() : null;

            // ── Hardware, firmware, battery ──────────────────────────────────
            int? hwId = null;
            if (root.TryGetProperty("hw_id", out var hwProp))
            {
                if (hwProp.ValueKind == JsonValueKind.Number)
                    hwId = hwProp.GetInt32();
                else if (hwProp.ValueKind == JsonValueKind.String && int.TryParse(hwProp.GetString(), out var hwIdParsed))
                    hwId = hwIdParsed;
            }

            int? battery = root.TryGetProperty("batt", out var battProp) && battProp.ValueKind == JsonValueKind.Number
                ? battProp.GetInt32() : null;

            // "firmware" can be integer (35) or string ("4.35")
            string? rawFw = null;
            if (root.TryGetProperty("firmware", out var fwProp))
                rawFw = fwProp.ValueKind == JsonValueKind.String ? fwProp.GetString() : fwProp.GetInt32().ToString();
            string? fwSub   = root.TryGetProperty("fw_sub", out var fwSubProp) ? fwSubProp.GetString() : null;
            string? firmware = MeshcomLookup.FormatFirmware(rawFw, fwSub);

            // ── GPS coordinates ──────────────────────────────────────────────
            // MeshCom node format uses separate direction fields:
            //   "lat":50.8515, "lat_dir":"N",  "long":9.1075, "long_dir":"E"
            // Some LoRa-relayed packets use signed "lat"/"lon" without direction.
            double? lat = null;
            double? lon = null;
            int?    alt = null;

            if (root.TryGetProperty("lat", out var latProp) && latProp.ValueKind == JsonValueKind.Number)
            {
                lat = latProp.GetDouble();
                if (root.TryGetProperty("lat_dir", out var latDirProp) &&
                    latDirProp.GetString()?.Equals("S", StringComparison.OrdinalIgnoreCase) == true)
                    lat = -lat;
            }

            // Longitude: MeshCom node uses "long"; LoRa-relayed packets may use "lon"
            if (root.TryGetProperty("long", out var longProp) && longProp.ValueKind == JsonValueKind.Number)
            {
                lon = longProp.GetDouble();
                if (root.TryGetProperty("long_dir", out var longDirProp) &&
                    longDirProp.GetString()?.Equals("W", StringComparison.OrdinalIgnoreCase) == true)
                    lon = -lon;
            }
            else if (root.TryGetProperty("lon", out var lonProp) && lonProp.ValueKind == JsonValueKind.Number)
            {
                lon = lonProp.GetDouble();
                if (root.TryGetProperty("lon_dir", out var lonDirProp) &&
                    lonDirProp.GetString()?.Equals("W", StringComparison.OrdinalIgnoreCase) == true)
                    lon = -lon;
            }

            if (root.TryGetProperty("alt", out var altProp) && altProp.ValueKind == JsonValueKind.Number)
            {
                // MeshCom uses APRS convention: altitude in feet -> convert to metres
                alt = (int)Math.Round(altProp.GetInt32() * 0.3048);
            }

            // 0°N 0°E (null island) = no GPS fix
            if (lat is 0.0 && lon is 0.0) { lat = null; lon = null; alt = null; }

            // ── Telemetry fields ─────────────────────────────────────────────
            double? temp1    = null;
            double? temp2    = null;
            double? humidity = null;
            double? pressure = null;
            if (isTelemetry)
            {
                if (root.TryGetProperty("temp1", out var t1) && t1.ValueKind == JsonValueKind.Number) temp1    = t1.GetDouble();
                if (root.TryGetProperty("temp2", out var t2) && t2.ValueKind == JsonValueKind.Number) temp2    = t2.GetDouble();
                if (root.TryGetProperty("hum",   out var hm) && hm.ValueKind == JsonValueKind.Number) humidity = hm.GetDouble();

                // Pressure: prefer qnh (sea-level). The "qfe" key is only a real station
                // pressure for the node's own telemetry (src_type "node"); for a heard
                // station (src_type "lora") it carries the APRS "/F=" field, which the
                // firmware fills with the barometric altitude in metres, not a pressure –
                // the real "/P=" value is never forwarded in the tele JSON (upstream bug;
                // MCProxy drops "qfe" for "lora" the same way). Only accept plausible hPa.
                if (root.TryGetProperty("qnh", out var qnh) && qnh.ValueKind == JsonValueKind.Number && IsPlausiblePressure(qnh.GetDouble()))
                    pressure = qnh.GetDouble();
                else if (!string.Equals(srcType, "lora", StringComparison.OrdinalIgnoreCase) &&
                         root.TryGetProperty("qfe", out var qfe) && qfe.ValueKind == JsonValueKind.Number && IsPlausiblePressure(qfe.GetDouble()))
                    pressure = qfe.GetDouble();
            }

            return new MeshcomMessage
            {
                From             = sender,
                To               = dst,
                Text             = msg,
                IsOutgoing       = false,
                RawData          = raw,
                Rssi             = rssi,
                Snr              = snr,
                Latitude         = lat,
                Longitude        = lon,
                Altitude         = alt,
                IsPositionBeacon = isPositionBeacon,
                IsTelemetry      = isTelemetry,
                IsAck            = isAck,
                IsTimeSync       = isTimeSync,
                MsgId            = msgId,
                SequenceNumber   = seqNum,
                RelayPath        = relayPath,
                ViaPath          = viaPath,
                SrcType          = srcType,
                HwId             = hwId,
                Battery          = battery,
                Firmware         = firmware,
                Temp1            = temp1,
                Temp2            = temp2,
                Humidity         = humidity,
                Pressure         = pressure,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON message: {Data}", raw);
            return null;
        }
    }
}
