using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Kiss;

/// <summary>Snapshot of the KISS hub state for the Settings UI.</summary>
public sealed record KissHubStatus(
    bool Listening,
    int Port,
    string Bind,
    string? TargetNodeName,
    bool TargetConnected,
    IReadOnlyList<string> Clients,
    string? Error);

/// <summary>
/// WebDesk as a KISS/TCP hub: opens a listener and fans one KISS node's traffic out to several
/// downstream KISS clients (Direwolf, YAAC, APRSdroid …). Only <c>type 0x00</c> data frames are
/// relayed in either direction; RxMeta (0x10) and TX-result (0xF0) stay WebDesk-internal, except
/// that a 0xF0 for a downstream-injected frame is routed back to the client that sent it.
/// See <c>docs/kiss-mode-analysis.md</c> §5.7.
/// </summary>
public sealed class KissHubService : BackgroundService
{
    private const int MaxQueuedFramesPerClient = 256;

    private readonly ILogger<KissHubService> _logger;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly NodeManager _nodes;
    private readonly KissClientService _kiss;

    private sealed class HubClient(string id, TcpClient tcp)
    {
        public string Id { get; } = id;
        public TcpClient Tcp { get; } = tcp;
        public Channel<byte[]> Outbound { get; } = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(MaxQueuedFramesPerClient) { FullMode = BoundedChannelFullMode.DropOldest });
    }

    private readonly ConcurrentDictionary<string, HubClient> _clients = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private string _boundConfig = "";     // "port|bind" of the currently-running listener
    private string? _lastError;

    public event Action? OnStatusChange;

    public KissHubService(
        ILogger<KissHubService> logger,
        IOptionsMonitor<MeshcomSettings> settings,
        NodeManager nodes,
        KissClientService kiss)
    {
        _logger   = logger;
        _settings = settings;
        _nodes    = nodes;
        _kiss     = kiss;
    }

    // ── Target node resolution ───────────────────────────────────────────

    private NodeProfile? TargetNode()
    {
        var cfg = _settings.CurrentValue.KissHub;
        var nodes = _settings.CurrentValue.Nodes;
        NodeProfile? node = cfg.NodeId is { } id && id != Guid.Empty
            ? nodes.FirstOrDefault(n => n.Id == id)
            : _nodes.PrimaryNode;
        return node is { Enabled: true, Transport: NodeTransport.Kiss } ? node : null;
    }

    public KissHubStatus GetStatus()
    {
        var cfg = _settings.CurrentValue.KissHub;
        var node = TargetNode();
        return new KissHubStatus(
            Listening: _listener is not null,
            Port: cfg.Port,
            Bind: cfg.BindLan ? "0.0.0.0" : "127.0.0.1",
            TargetNodeName: node?.Name,
            TargetConnected: node is not null && _kiss.IsConnected(node.Id),
            Clients: _clients.Values.Select(c => c.Id).OrderBy(x => x).ToList(),
            Error: _lastError);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _kiss.OnNodeDataFrame += OnNodeDataFrame;
        _kiss.OnHubTxResult   += OnHubTxResult;

        Sync(stoppingToken);
        using var reg = _settings.OnChange(_ => Sync(stoppingToken));

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        _kiss.OnNodeDataFrame -= OnNodeDataFrame;
        _kiss.OnHubTxResult   -= OnHubTxResult;
        StopListener();
    }

    private readonly object _syncLock = new();

    private void Sync(CancellationToken stoppingToken)
    {
        lock (_syncLock)
        {
            var cfg = _settings.CurrentValue.KissHub;
            var want = cfg.Enabled ? $"{cfg.Port}|{(cfg.BindLan ? "0.0.0.0" : "127.0.0.1")}" : "";

            if (want == _boundConfig) { RaiseStatusChange(); return; }

            StopListener();
            _boundConfig = want;
            if (want == "") { RaiseStatusChange(); return; }

            try
            {
                var addr = cfg.BindLan ? IPAddress.Any : IPAddress.Loopback;
                _listener = new TcpListener(addr, cfg.Port);
                _listener.Start();
                _listenerCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                _lastError = null;
                _logger.LogInformation("KISS hub listening on {Addr}:{Port} (target node '{Node}')",
                    addr, cfg.Port, TargetNode()?.Name ?? "(none)");
                _ = AcceptLoopAsync(_listener, _listenerCts.Token);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _listener = null;
                _boundConfig = "";
                _logger.LogError(ex, "KISS hub: cannot listen on port {Port}", cfg.Port);
            }
            RaiseStatusChange();
        }
    }

    private void StopListener()
    {
        _listenerCts?.Cancel();
        try { _listener?.Stop(); } catch { /* ignore */ }
        _listener = null;
        _listenerCts = null;
        foreach (var c in _clients.Values)
            try { c.Tcp.Close(); } catch { /* ignore */ }
        _clients.Clear();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "KISS hub accept failed");
                break;
            }

            var id = (tcp.Client.RemoteEndPoint as IPEndPoint)?.ToString() ?? Guid.NewGuid().ToString("N")[..8];
            var client = new HubClient(id, tcp);
            _clients[id] = client;
            _logger.LogInformation("KISS hub: client {Id} connected ({Count} total)", id, _clients.Count);
            RaiseStatusChange();
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(HubClient client, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            client.Tcp.NoDelay = true;
            var stream = client.Tcp.GetStream();
            var writer = WriterLoopAsync(client, stream, linked.Token);

            var deframer = new KissDeframer();
            var buf = new byte[4096];
            while (!linked.Token.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buf, linked.Token);
                if (n == 0) break;
                foreach (var frame in deframer.Push(buf.AsSpan(0, n)))
                {
                    if (frame.Length < 1 || frame[0] != KissFraming.TypeData) continue; // only data frames from downstream
                    var node = TargetNode();
                    if (node is null || !_kiss.IsConnected(node.Id))
                    {
                        _logger.LogDebug("KISS hub: dropping TX from {Id} – target node not available", client.Id);
                        continue;
                    }
                    await _kiss.SendHubFrameAsync(node.Id, frame[1..], client.Id);
                }
            }

            linked.Cancel();
            await writer;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "KISS hub client {Id} error", client.Id);
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            try { client.Tcp.Close(); } catch { }
            _logger.LogInformation("KISS hub: client {Id} disconnected ({Count} left)", client.Id, _clients.Count);
            RaiseStatusChange();
        }
    }

    private static async Task WriterLoopAsync(HubClient client, NetworkStream stream, CancellationToken ct)
    {
        try
        {
            await foreach (var frame in client.Outbound.Reader.ReadAllAsync(ct))
                await stream.WriteAsync(frame, ct);
        }
        catch (OperationCanceledException) { }
        catch { /* socket gone – HandleClientAsync cleans up */ }
    }

    // ── Fan-out from the node ────────────────────────────────────────────

    private void OnNodeDataFrame(Guid nodeId, byte[] ax25Payload)
    {
        var node = TargetNode();
        if (node is null || node.Id != nodeId || _clients.IsEmpty) return;

        var kissFrame = KissFraming.Frame(KissFraming.TypeData, ax25Payload);
        foreach (var c in _clients.Values)
            c.Outbound.Writer.TryWrite(kissFrame);
    }

    private void OnHubTxResult(string clientId, byte[] kissResultFrame)
    {
        if (_clients.TryGetValue(clientId, out var c))
            c.Outbound.Writer.TryWrite(kissResultFrame);
    }

    private void RaiseStatusChange()
    {
        try { OnStatusChange?.Invoke(); } catch { /* UI handler threw */ }
    }
}
