using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Kiss;

/// <summary>
/// Live state of the KISS/TCP connection to one node.
/// </summary>
public enum KissConnectionState
{
    Disabled,     // node is not on the KISS transport
    Connecting,
    Connected,
    NodeGone,     // socket closed / read error – node rebooted or network loss
    SlotBusy,     // connection refused / immediately closed – another KISS client holds the single slot
    Error,
}

/// <summary>Per-node KISS status snapshot for the Settings / node-switcher UI.</summary>
public sealed record KissNodeStatus(
    KissConnectionState State,
    DateTime? LastRxUtc,
    int RxFrames,
    string? Detail);

/// <summary>
/// Background service that maintains one KISS/TCP connection per node whose
/// <see cref="NodeProfile.Transport"/> is <see cref="NodeTransport.Kiss"/>, decodes the
/// received frames (KISS de-framing + AX.25 UI + APRS) and feeds them into the same
/// <see cref="ChatService"/> pipeline the UDP transport uses. See
/// <c>docs/kiss-mode-analysis.md</c>.
/// </summary>
public sealed class KissClientService : BackgroundService
{
    private readonly ILogger<KissClientService> _logger;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ChatService _chat;
    private readonly NodeManager _nodes;
    private readonly MeshcomUdpService _udp;   // shared ConnectionStatus + own-position sink

    private sealed record Worker(NodeProfile Node, CancellationTokenSource Cts, Task Task);

    private readonly ConcurrentDictionary<Guid, Worker> _workers = new();
    private readonly ConcurrentDictionary<Guid, KissNodeStatus> _status = new();
    private readonly ConcurrentDictionary<Guid, WorkerConnection> _connections = new();

    /// <summary>Raised whenever a per-node <see cref="KissNodeStatus"/> changes.</summary>
    public event Action? OnStatusChange;

    /// <summary>
    /// Raised for every received KISS data frame (type 0x00). Args: node id, and the AX.25 UI
    /// frame bytes (SLIP-de-escaped, WITHOUT the KISS type byte). Consumed by <c>KissHubService</c>
    /// for fan-out to downstream KISS clients.
    /// </summary>
    public event Action<Guid, byte[]>? OnNodeDataFrame;

    /// <summary>
    /// Raised when the node returns a TX-result (0xF0) for a frame that a hub client injected.
    /// Args: the hub client id, and the ready-to-send KISS 0xF0 frame to forward to that client.
    /// </summary>
    public event Action<string, byte[]>? OnHubTxResult;

    public KissClientService(
        ILogger<KissClientService> logger,
        IOptionsMonitor<MeshcomSettings> settings,
        ChatService chat,
        NodeManager nodes,
        MeshcomUdpService udp)
    {
        _logger   = logger;
        _settings = settings;
        _chat     = chat;
        _nodes    = nodes;
        _udp      = udp;
    }

    public KissNodeStatus GetStatus(Guid nodeId) =>
        _status.TryGetValue(nodeId, out var s) ? s : new KissNodeStatus(KissConnectionState.Disabled, null, 0, null);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SyncWorkers(stoppingToken);
        using var reg = _settings.OnChange(_ => SyncWorkers(stoppingToken));

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* shutting down */ }

        foreach (var w in _workers.Values)
        {
            w.Cts.Cancel();
            try { await w.Task; } catch { /* ignore */ }
        }
    }

    /// <summary>Starts/stops per-node workers so they match the current KISS node list.</summary>
    private void SyncWorkers(CancellationToken stoppingToken)
    {
        var desired = _settings.CurrentValue.Nodes
            .Where(n => n.Enabled && n.Transport == NodeTransport.Kiss)
            .ToList();

        // Stop workers whose node was removed, disabled, or had its endpoint changed.
        foreach (var (id, worker) in _workers.ToArray())
        {
            var match = desired.FirstOrDefault(n => n.Id == id);
            bool endpointChanged = match is not null &&
                (match.DeviceIp != worker.Node.DeviceIp || match.KissPort != worker.Node.KissPort ||
                 match.Callsign != worker.Node.Callsign);
            if (match is null || endpointChanged)
            {
                worker.Cts.Cancel();
                _workers.TryRemove(id, out _);
                if (match is null) { _status.TryRemove(id, out _); RaiseStatusChange(); }
            }
        }

        // Start workers for newly-added KISS nodes.
        foreach (var node in desired)
        {
            if (_workers.ContainsKey(node.Id)) continue;
            var cts  = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var copy = node;
            var task = Task.Run(() => RunWorkerAsync(copy, cts.Token), cts.Token);
            _workers[node.Id] = new Worker(copy, cts, task);
        }
    }

    private async Task RunWorkerAsync(NodeProfile node, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        var maxBackoff = TimeSpan.FromSeconds(30);

        while (!ct.IsCancellationRequested)
        {
            SetStatus(node.Id, KissConnectionState.Connecting, null);
            try
            {
                using var tcp = new TcpClient();
                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(10));
                    await tcp.ConnectAsync(node.DeviceIp, node.KissPort, connectCts.Token);
                }

                tcp.NoDelay = true;
                await using var stream = tcp.GetStream();

                // Optional HMAC auth: the node sends "NONCE: <hex>" in clear text before any KISS
                // byte when its operator set "--kiss auth on". First byte 0xC0 = no auth.
                bool authOk, authUsed; byte? stashed;
                try
                {
                    (authOk, authUsed, stashed) = await KissAuthAsync(stream, node.TelnetPassword ?? string.Empty, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new KissAuthException();   // handshake stalled (no OK/FAIL within 15 s)
                }
                if (!authOk)
                    throw new KissAuthException();

                var conn = new WorkerConnection(stream);
                _connections[node.Id] = conn;
                SetStatus(node.Id, KissConnectionState.Connected, null);
                _logger.LogInformation("KISS connected to node '{Name}' ({Ip}:{Port}){Auth}", node.Name, node.DeviceIp, node.KissPort,
                    authUsed ? " [authenticated]" : "");
                backoff = TimeSpan.FromSeconds(1);

                var deframer = new KissDeframer();
                var rx = new byte[4096];
                var rxCtx = new KissRxContext();
                var firstRead = true;

                // A byte read during auth detection that turned out to be KISS data.
                if (stashed is { } sb0)
                    foreach (var frame in deframer.Push([sb0]))
                        HandleFrame(node, frame, rxCtx);

                while (!ct.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(rx, ct);
                    if (n == 0)
                    {
                        // Immediate EOF on the very first read = the single client slot is taken.
                        SetStatus(node.Id,
                            firstRead ? KissConnectionState.SlotBusy : KissConnectionState.NodeGone,
                            firstRead ? "another KISS client is connected to this node" : null);
                        break;
                    }
                    firstRead = false;

                    foreach (var frame in deframer.Push(rx.AsSpan(0, n)))
                        HandleFrame(node, frame, rxCtx);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (KissAuthException)
            {
                SetStatus(node.Id, KissConnectionState.Error, "KISS authentication rejected – check the node password (--passwd)");
                _logger.LogWarning("KISS auth rejected by node '{Name}'", node.Name);
            }
            catch (SocketException ex)
            {
                SetStatus(node.Id, KissConnectionState.NodeGone, ex.SocketErrorCode.ToString());
                _logger.LogDebug("KISS socket error for node '{Name}': {Err}", node.Name, ex.SocketErrorCode);
            }
            catch (Exception ex)
            {
                SetStatus(node.Id, KissConnectionState.Error, ex.Message);
                _logger.LogWarning(ex, "KISS worker error for node '{Name}'", node.Name);
            }
            finally
            {
                _connections.TryRemove(node.Id, out _);
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, maxBackoff.Ticks));
        }

        SetStatus(node.Id, KissConnectionState.Disabled, null);
    }

    // ── Optional HMAC authentication ─────────────────────────────────────

    private sealed class KissAuthException : Exception { }

    /// <summary>
    /// Handles the optional pre-KISS auth handshake. The node sends
    /// <c>"NONCE: &lt;32 hex&gt;\r\n"</c> in clear text; we reply
    /// <c>HMAC-SHA256(passwd, nonce_bytes)</c> as 64 hex + CRLF and expect <c>"OK"</c>.
    /// Detection: the first byte is <c>0xC0</c> (FEND) → no auth; <c>'N'</c> → handshake.
    /// Returns <c>(ok, authUsed, stashedByte)</c> — <c>stashedByte</c> is a KISS byte that was
    /// read during detection and must be fed to the deframer.
    /// </summary>
    private async Task<(bool Ok, bool Used, byte? Stashed)> KissAuthAsync(
        NetworkStream stream, string password, CancellationToken ct)
    {
        // An auth node sends "NONCE:" *immediately* after accept. A no-auth node may stay silent
        // for minutes on a quiet mesh, so only wait ~4 s for the first byte: nothing → no auth.
        var one = new byte[1];
        try
        {
            using var firstByteTo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            firstByteTo.CancelAfter(TimeSpan.FromSeconds(4));
            if (await stream.ReadAsync(one.AsMemory(0, 1), firstByteTo.Token) == 0) return (false, false, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (true, false, null);   // silent node → assume no auth, proceed to the read loop
        }

        if (one[0] != (byte)'N') return (true, false, one[0]);   // 0xC0 or anything else → plain KISS

        // From here it's the auth handshake – 15 s for the whole exchange.
        using var to = CancellationTokenSource.CreateLinkedTokenSource(ct);
        to.CancelAfter(TimeSpan.FromSeconds(15));

        // Read the rest of the "NONCE: <hex>" line (we already consumed 'N').
        var line = new StringBuilder("N");
        var b = new byte[1];
        while (line.Length < 128)
        {
            if (await stream.ReadAsync(b.AsMemory(0, 1), to.Token) == 0) return (false, false, null);
            if (b[0] is (byte)'\r' or (byte)'\n') break;
            line.Append((char)b[0]);
        }

        var parts = line.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[1].Length != 32) return (false, true, null);

        byte[] nonce;
        try { nonce = Convert.FromHexString(parts[1]); }
        catch { return (false, true, null); }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password.Trim()));
        var resp = Convert.ToHexString(hmac.ComputeHash(nonce)).ToLowerInvariant();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(resp + "\r\n"), to.Token);

        // Read the "OK" / "FAIL" reply line, skipping any leftover CR/LF from the NONCE line.
        var reply = new StringBuilder();
        while (reply.Length < 16)
        {
            if (await stream.ReadAsync(b.AsMemory(0, 1), to.Token) == 0) break;
            if (b[0] is (byte)'\r') continue;
            if (b[0] is (byte)'\n') { if (reply.Length > 0) break; else continue; }
            reply.Append((char)b[0]);
        }
        return (reply.ToString().Trim().Equals("OK", StringComparison.OrdinalIgnoreCase), true, null);
    }

    // ── Frame handling ───────────────────────────────────────────────────

    /// <summary>Per-connection RX state: the most recently dispatched data message, for
    /// attaching the RxMeta frame that follows it (spec: meta belongs to the PRECEDING frame).</summary>
    private sealed class KissRxContext
    {
        public MeshcomMessage? LastMessage;
        public DateTime LastMessageAt;
    }

    private void HandleFrame(NodeProfile node, byte[] frame, KissRxContext ctx)
    {
        if (frame.Length == 0) return;
        byte type = frame[0];
        var payload = frame.AsSpan(1);

        switch (type)
        {
            case KissFraming.TypeRxMeta:
                // Belongs to the data frame we just dispatched – patch it in place.
                if (payload.Length >= 3 &&
                    ctx.LastMessage is { } lm &&
                    DateTime.UtcNow - ctx.LastMessageAt < TimeSpan.FromSeconds(5))
                {
                    double snr = (sbyte)payload[0];
                    int rssi = BinaryPrimitives.ReadInt16LittleEndian(payload[1..3]);
                    lm.Snr  = snr;
                    lm.Rssi = rssi < 0 ? rssi : null;
                    ctx.LastMessage = null;
                    _chat.ApplySignalMetadata(node.Id, lm.From, lm.Rssi, lm.Snr);
                    if (_nodes.PrimaryNode?.Id == node.Id && lm.Rssi.HasValue)
                    {
                        _udp.Status.LastRssi = lm.Rssi;
                        _udp.Status.LastSnr  = lm.Snr;
                    }
                }
                return;

            case KissFraming.TypeTxResult:
                HandleTxResult(node, payload);
                return;

            case KissFraming.TypeData:
                if (OnNodeDataFrame is { } hubFanout)
                {
                    var copy = payload.ToArray();
                    try { hubFanout(node.Id, copy); } catch { /* hub handler threw */ }
                }
                HandleDataFrame(node, payload, ctx);
                return;

            default:
                _logger.LogDebug("KISS: ignoring frame type 0x{Type:X2} from '{Name}'", type, node.Name);
                return;
        }
    }

    private void HandleDataFrame(NodeProfile node, ReadOnlySpan<byte> payload, KissRxContext ctx)
    {
        var ax = Ax25Ui.Decode(payload);
        if (ax is null)
        {
            _chat.AddRawMessage(new MeshcomMessage
            {
                RawData = "KISS " + Convert.ToHexString(payload),
                Text    = "KISS " + Convert.ToHexString(payload),
                NodeId  = node.Id,
            });
            return;
        }

        var info = AprsInfo.Parse(ax.Info);
        var digis = ax.Digipeaters.Count > 0 ? string.Join(",", ax.Digipeaters) : null;

        _logger.LogDebug("KISS RX [{Node}] {Kind} {Src}>{Dst}{Path} : {Info}",
            node.Name, info.Kind, ax.Src, ax.Dest,
            digis is null ? "" : $" via {digis}", ax.Info);

        var msg = new MeshcomMessage
        {
            NodeId         = node.Id,
            From           = ax.Src,
            SrcType        = "lora",
            RawData        = ReconstructTnc2(ax),
            DigipeaterPath = digis,
            // MH list / map convention: index 0 = origin, then relays.
            RelayPath      = digis is null ? null : $"{ax.Src},{digis}",
        };

        switch (info.Kind)
        {
            case AprsInfoKind.Position when info.Position is { } p:
                msg.To               = "*";
                msg.IsPositionBeacon = true;
                msg.Latitude         = p.Lat;
                msg.Longitude        = p.Lon;
                msg.Altitude         = info.AltitudeMeters;
                msg.AprsComment      = string.IsNullOrWhiteSpace(p.Comment) ? null : p.Comment;
                msg.Battery          = info.BatteryPercent;
                msg.NeighbourCount   = info.NeighbourCount;
                msg.RelayNodeList    = info.RelayNodeList;
                msg.Temp1            = info.Temperature;
                msg.Humidity         = info.Humidity;
                msg.Pressure         = info.Pressure;

                if (string.Equals(ax.Src, node.Callsign, StringComparison.OrdinalIgnoreCase) &&
                    _nodes.PrimaryNode?.Id == node.Id)
                    _udp.SetOwnPosition(p.Lat, p.Lon, info.AltitudeMeters, "Node (KISS)");

                _chat.AddPositionBeacon(msg);
                break;

            case AprsInfoKind.Message when info.Message is { } tm:
                msg.To             = string.IsNullOrEmpty(tm.Addressee) ? "*" : tm.Addressee;
                msg.Text           = tm.Text;
                msg.SequenceNumber = tm.SequenceNumber;
                msg.IsAck          = tm.IsAck;
                if (!tm.IsAck && TimeSyncRx.IsMatch(tm.Text))
                {
                    // MeshCom network time-sync broadcast ("{CET}2026-08-30 18:43:36") –
                    // monitor only, no chat tab (same as the ext-udp path).
                    msg.IsTimeSync = true;
                    _chat.AddRawMessage(msg);
                }
                else if (tm.IsAck)
                {
                    _logger.LogDebug("KISS RX ACK from {From} → target {To} seq {Seq}",
                        msg.From, msg.To, msg.SequenceNumber ?? "(none)");
                    // Only mark our own outgoing messages when the ACK is actually addressed to
                    // one of our nodes – an ACK for a hub client (foreign callsign) is monitor-only.
                    if (IsOwnCallsign(tm.Addressee))
                        _chat.AddAck(msg);
                    else
                        _chat.AddRawMessage(msg);
                }
                else if (string.Equals(ax.Src, node.Callsign, StringComparison.OrdinalIgnoreCase))
                {
                    // Echo of a message this node transmitted (e.g. heard back off a relay) –
                    // the TX row is already shown; just confirm the outgoing message.
                    _chat.AssignOutgoingSequence(msg.To, tm.SequenceNumber, node.Id);
                }
                else
                {
                    _chat.AddIncomingMessage(msg);
                }
                break;

            default:
                msg.Text = ax.Info;
                _chat.AddRawMessage(msg);
                break;
        }

        _nodes.MarkNodeSeen(node.Id);
        if (_nodes.PrimaryNode?.Id == node.Id)
            _udp.RecordTransportRx(msg);

        // Remember for a possible RxMeta frame that follows (spec: meta ↦ preceding frame).
        ctx.LastMessage   = msg;
        ctx.LastMessageAt = DateTime.UtcNow;

        var prev = GetStatus(node.Id);
        _status[node.Id] = prev with { LastRxUtc = DateTime.UtcNow, RxFrames = prev.RxFrames + 1 };
        RaiseStatusChange();
    }

    /// <summary>MeshCom network time-sync broadcast, e.g. "{CET}2026-08-30 18:43:36".</summary>
    private static readonly Regex TimeSyncRx =
        new(@"^\{[A-Z]{2,5}\}\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);

    /// <summary>
    /// True when <paramref name="call"/> is exactly one of our own node callsigns (incl. SSID).
    /// An ACK for a hub client shares the operator's base call but a different SSID, so a base
    /// match is not enough here.
    /// </summary>
    private bool IsOwnCallsign(string? call) =>
        !string.IsNullOrEmpty(call) &&
        _settings.CurrentValue.Nodes.Any(n => string.Equals(n.Callsign, call, StringComparison.OrdinalIgnoreCase));

    private static string ReconstructTnc2(Ax25Frame ax)
    {
        var path = ax.Digipeaters.Count > 0 ? "," + string.Join(",", ax.Digipeaters) : string.Empty;
        return $"{ax.Src}>{ax.Dest}{path}:{ax.Info}";
    }

    // ── TX (Phase B) ─────────────────────────────────────────────────────

    /// <summary>One entry in a connection's TX-result FIFO – either a chat message awaiting its
    /// delivery status, or a frame a hub client injected (result routed back to that client).</summary>
    private sealed record PendingKissTx(MeshcomMessage? ChatMessage, string? HubClientId);

    private sealed class WorkerConnection(NetworkStream stream)
    {
        public NetworkStream Stream { get; } = stream;
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
        public ConcurrentQueue<PendingKissTx> PendingTx { get; } = new();

        // The node rejects injection faster than 8 frames/s (0xF0 status 0x05). Pace our own
        // writes to a strict 125 ms spacing. Only touched under WriteLock.
        private long _nextSendTicks;
        public async Task PaceAsync()
        {
            var now  = Environment.TickCount64;
            var wait = _nextSendTicks - now;
            if (wait > 0) await Task.Delay((int)wait);
            _nextSendTicks = Math.Max(now, _nextSendTicks) + 125;
        }
    }

    /// <summary>
    /// True when a live KISS connection to <paramref name="nodeId"/> exists.
    /// </summary>
    public bool IsConnected(Guid nodeId) => _connections.ContainsKey(nodeId);

    /// <summary>
    /// Encodes <paramref name="text"/> as an APRS message and injects it into the mesh over
    /// the node's KISS connection. The result (accepted + msg_id, or rejected + reason) is
    /// attached to <paramref name="outgoing"/> asynchronously when the node's TX-result frame
    /// arrives. Returns <c>false</c> when the frame could not even be sent.
    /// </summary>
    public async Task<bool> SendMessageFrameAsync(NodeProfile node, string destination, string text, MeshcomMessage outgoing)
    {
        var info = BuildMessageInfo(destination, text);
        return await SendInfoFrameAsync(node, info, outgoing);
    }

    private static string BuildMessageInfo(string destination, string text)
    {
        // ":ADDRESSEE :text" – addressee is 9 chars, space-padded.
        var addressee = destination == "*" ? "*" : destination.TrimStart('#');
        if (addressee.Length > 9) addressee = addressee[..9];
        return $":{addressee.PadRight(9)}:{text}";
    }

    private async Task<bool> SendInfoFrameAsync(NodeProfile node, string info, MeshcomMessage outgoing)
    {
        if (!_connections.TryGetValue(node.Id, out var conn))
        {
            outgoing.TxResult = KissTxResult.NoResponse;
            _logger.LogWarning("KISS TX: no connection to node '{Name}'", node.Name);
            return false;
        }

        var ax = Ax25Ui.Encode(dest: "APRS", src: node.Callsign, info);
        if (ax is null)
        {
            outgoing.TxResult = KissTxResult.RejectedFrame;
            _logger.LogWarning("KISS TX: cannot encode AX.25 frame for '{Call}' – base callsign > 6 chars or SSID > 15 (AX.25 limit)", node.Callsign);
            return false;
        }

        var kiss = KissFraming.Frame(KissFraming.TypeData, ax);
        var entry = new PendingKissTx(outgoing, null);
        conn.PendingTx.Enqueue(entry);
        _logger.LogDebug("KISS TX [{Node}] as {Call}: {Info}", node.Name, node.Callsign, info);

        await conn.WriteLock.WaitAsync();
        try { await conn.PaceAsync(); await conn.Stream.WriteAsync(kiss); }
        catch (Exception ex)
        {
            conn.PendingTx.TryDequeue(out _);
            outgoing.TxResult = KissTxResult.NoResponse;
            _logger.LogWarning(ex, "KISS TX write failed for node '{Name}'", node.Name);
            return false;
        }
        finally { conn.WriteLock.Release(); }

        // Sweep away a result that never comes.
        _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ =>
        {
            if (outgoing.TxResult is null && conn.PendingTx.TryPeek(out var head) && ReferenceEquals(head, entry))
            {
                conn.PendingTx.TryDequeue(out PendingKissTx? _);
                outgoing.TxResult = KissTxResult.NoResponse;
                _chat.NotifyExternalChange();
            }
        }, TaskScheduler.Default);
        return true;
    }

    /// <summary>
    /// Injects a raw AX.25 UI frame received from a downstream hub client into the mesh over
    /// <paramref name="nodeId"/>'s KISS connection. The node's TX-result (0xF0) is routed back
    /// to <paramref name="hubClientId"/> via <see cref="OnHubTxResult"/>. Returns <c>false</c>
    /// when there is no live connection to the node.
    /// </summary>
    public async Task<bool> SendHubFrameAsync(Guid nodeId, byte[] ax25Payload, string hubClientId)
    {
        if (!_connections.TryGetValue(nodeId, out var conn)) return false;

        var kiss = KissFraming.Frame(KissFraming.TypeData, ax25Payload);
        conn.PendingTx.Enqueue(new PendingKissTx(null, hubClientId));

        await conn.WriteLock.WaitAsync();
        try { await conn.PaceAsync(); await conn.Stream.WriteAsync(kiss); }
        catch (Exception ex)
        {
            conn.PendingTx.TryDequeue(out _);
            _logger.LogWarning(ex, "KISS hub TX write failed for node {NodeId}", nodeId);
            return false;
        }
        finally { conn.WriteLock.Release(); }

        // Surface hub-injected traffic in the monitor (monitor-only, no chat tab).
        var ax = Ax25Ui.Decode(ax25Payload);
        if (ax is not null)
        {
            var info = AprsInfo.Parse(ax.Info);
            var m = new MeshcomMessage
            {
                NodeId     = nodeId,
                From       = ax.Src,
                IsOutgoing = true,
                SrcType    = "hub",
                RawData    = $"[hub {hubClientId}] {ReconstructTnc2(ax)}",
                Text       = ax.Info,
            };
            if (info is { Kind: AprsInfoKind.Position, Position: { } p })
            {
                m.To = "*"; m.IsPositionBeacon = true;
                m.Latitude = p.Lat; m.Longitude = p.Lon; m.Altitude = info.AltitudeMeters;
                m.AprsComment = string.IsNullOrWhiteSpace(p.Comment) ? null : p.Comment;
            }
            else if (info is { Kind: AprsInfoKind.Message, Message: { } tm })
            {
                m.To = string.IsNullOrEmpty(tm.Addressee) ? "*" : tm.Addressee;
                m.Text = tm.Text;
            }
            _chat.AddRawMessage(m);
        }
        _logger.LogDebug("KISS hub TX [{Node}] from client {Client}: {Frame}", nodeId, hubClientId,
            ax is null ? Convert.ToHexString(ax25Payload) : ReconstructTnc2(ax));
        return true;
    }

    private void HandleTxResult(NodeProfile node, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1) return;
        byte status = payload[0];
        uint? msgIdRaw = null;
        if (status == 0x01 && payload.Length >= 5)
            msgIdRaw = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..5]);
        string? msgId = msgIdRaw?.ToString("X8");

        if (!_connections.TryGetValue(node.Id, out var conn) || !conn.PendingTx.TryDequeue(out var pending))
        {
            _logger.LogDebug("KISS TX-result 0x{Status:X2} from '{Name}' with no pending frame", status, node.Name);
            return;
        }

        // Frame came from a downstream hub client → forward the raw 0xF0 result to that client only.
        if (pending.HubClientId is { } hubId)
        {
            OnHubTxResult?.Invoke(hubId, KissFraming.Frame(KissFraming.TypeTxResult, payload));
            if (status != 0x01)
                _logger.LogWarning("KISS hub TX rejected by node '{Name}' for client {Client}: status 0x{Status:X2}",
                    node.Name, hubId, status);
            return;
        }

        var outgoing = pending.ChatMessage;
        if (outgoing is null) return;

        outgoing.TxResult = status switch
        {
            0x01 => KissTxResult.Accepted,
            0x02 => KissTxResult.RejectedCallsign,
            0x03 => KissTxResult.RejectedTxOff,
            0x04 => KissTxResult.RejectedFrame,
            0x05 => KissTxResult.RejectedRateLimit,
            _    => KissTxResult.RejectedFrame,
        };
        if (msgId is not null) outgoing.MsgId = msgId;

        // The MeshCom on-air ACK is ":ack<NNN>" where NNN = msg_id & 0x3FF (the "{NNN"
        // the firmware appends to a DM). Stamping it as the sequence number lets
        // MarkMessageAcknowledged match the ACK to THIS message exactly.
        if (msgIdRaw is { } raw && outgoing.TxResult == KissTxResult.Accepted)
            outgoing.SequenceNumber = (raw & 0x3FF).ToString();

        if (outgoing.TxResult == KissTxResult.Accepted)
            _logger.LogDebug("KISS TX-result [{Node}]: accepted, msg_id {MsgId} (ack seq {Seq})",
                node.Name, msgId ?? "(none)", outgoing.SequenceNumber ?? "(none)");
        else
            _logger.LogWarning("KISS TX rejected by node '{Name}': {Result}", node.Name, outgoing.TxResult);

        _chat.NotifyExternalChange();
    }

    // ── Status plumbing ──────────────────────────────────────────────────

    private void SetStatus(Guid nodeId, KissConnectionState state, string? detail)
    {
        var prev = GetStatus(nodeId);
        if (prev.State == state && prev.Detail == detail) return;
        _status[nodeId] = prev with { State = state, Detail = detail };
        RaiseStatusChange();
    }

    private void RaiseStatusChange()
    {
        try { OnStatusChange?.Invoke(); } catch { /* UI handler threw – ignore */ }
    }
}
