using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Centralises multi-node awareness for the application.
/// <para>
/// The manager reads <see cref="MeshcomSettings.Nodes"/> and exposes helper methods
/// to UI components and services that need to know which nodes exist and which one is
/// currently active in the Chat view.
/// </para>
/// <para>
/// <b>Primary node:</b> the node that drives all global features (map, MH list, bot,
/// telemetry, beacon, …).  Exactly one node must carry <see cref="NodeProfile.IsPrimary"/>
/// = true; if the list is empty the app falls back to the legacy single-node connection
/// settings for backward compatibility.
/// </para>
/// <para>
/// <b>Selected node (chat view):</b> the node whose chat messages are displayed.
/// The user can switch this at runtime via the node-switcher UI.
/// On startup the selected node is the primary node.
/// </para>
/// </summary>
public sealed class NodeManager
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;

    /// <summary>Raised whenever the selected (chat-view) node changes or a node's online status changes.</summary>
    public event Action? OnSelectedNodeChanged;

    private Guid? _selectedNodeId;

    // ── Last-seen tracking ───────────────────────────────────────────────
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeen = new();

    /// <summary>Records that a packet was received from the given node right now.</summary>
    public void MarkNodeSeen(Guid nodeId)
    {
        var previous = _lastSeen.GetValueOrDefault(nodeId);
        _lastSeen[nodeId] = DateTime.UtcNow;
        // Only fire OnChange when the status bucket changes (offline→online, online→stale, etc.)
        if (GetNodeStatus(nodeId, previous) != GetNodeStatus(nodeId, DateTime.UtcNow))
            OnSelectedNodeChanged?.Invoke();
        else
            OnSelectedNodeChanged?.Invoke(); // always refresh so the tooltip stays current
    }

    /// <summary>Returns the UTC timestamp of the last received packet for this node, or null if never seen.</summary>
    public DateTime? GetLastSeen(Guid nodeId) =>
        _lastSeen.TryGetValue(nodeId, out var t) ? t : null;

    /// <summary>Returns the online status of a node based on the last received packet time.</summary>
    public NodeOnlineStatus GetNodeStatus(Guid nodeId)
    {
        if (!_lastSeen.TryGetValue(nodeId, out var t)) return NodeOnlineStatus.Unknown;
        var age = DateTime.UtcNow - t;
        if (age.TotalMinutes <= 5)  return NodeOnlineStatus.Online;
        if (age.TotalMinutes <= 30) return NodeOnlineStatus.Stale;
        return NodeOnlineStatus.Offline;
    }

    private static NodeOnlineStatus GetNodeStatus(Guid _, DateTime? t)
    {
        if (t is null) return NodeOnlineStatus.Unknown;
        var age = DateTime.UtcNow - t.Value;
        if (age.TotalMinutes <= 5)  return NodeOnlineStatus.Online;
        if (age.TotalMinutes <= 30) return NodeOnlineStatus.Stale;
        return NodeOnlineStatus.Offline;
    }

    public NodeManager(IOptionsMonitor<MeshcomSettings> settingsMonitor)
    {
        _settingsMonitor = settingsMonitor;

        // When settings reload, re-validate the selected node.
        _settingsMonitor.OnChange(_ => EnsureSelectedNodeValid());
    }

    // ── Node list helpers ────────────────────────────────────────────────

    /// <summary>
    /// Returns the current node list.
    /// When the list is empty the application is running in legacy single-node mode.
    /// </summary>
    public IReadOnlyList<NodeProfile> Nodes => _settingsMonitor.CurrentValue.Nodes;

    /// <summary>True when at least one node profile is configured.</summary>
    public bool MultiNodeEnabled => _settingsMonitor.CurrentValue.Nodes.Count > 0;

    /// <summary>
    /// Returns the primary node, or <c>null</c> when running in legacy mode.
    /// In legacy mode callers should fall back to the top-level connection settings.
    /// </summary>
    public NodeProfile? PrimaryNode =>
        _settingsMonitor.CurrentValue.Nodes.FirstOrDefault(n => n.IsPrimary);

    // ── Selected node (chat view) ────────────────────────────────────────

    /// <summary>
    /// The node whose chat context is currently shown.
    /// Falls back to <see cref="PrimaryNode"/> when nothing is explicitly selected.
    /// Returns <c>null</c> only in legacy single-node mode.
    /// </summary>
    public NodeProfile? SelectedNode
    {
        get
        {
            var nodes = _settingsMonitor.CurrentValue.Nodes;
            if (nodes.Count == 0) return null;

            if (_selectedNodeId is { } id)
            {
                var found = nodes.FirstOrDefault(n => n.Id == id);
                if (found is not null) return found;
            }

            // Default to primary.
            return nodes.FirstOrDefault(n => n.IsPrimary) ?? nodes[0];
        }
    }

    /// <summary>Switches the chat view to the node with <paramref name="nodeId"/>.</summary>
    public void SelectNode(Guid nodeId)
    {
        _selectedNodeId = nodeId;
        OnSelectedNodeChanged?.Invoke();
    }

    /// <summary>Switches the chat view back to the primary node.</summary>
    public void SelectPrimaryNode()
    {
        _selectedNodeId = null;
        OnSelectedNodeChanged?.Invoke();
    }

    // ── IP-based node resolution ─────────────────────────────────────────

    /// <summary>
    /// Resolves a <see cref="NodeProfile"/> by the source IP address of an incoming UDP packet.
    /// <para>
    /// This is the primary way to identify <i>which</i> node sent a packet when all nodes share
    /// the same UDP port (the common MeshCom default of 1799).
    /// </para>
    /// Returns <c>null</c> when no configured node matches (e.g. in legacy single-node mode or
    /// when the IP is not in the node list – callers should fall back to
    /// <see cref="MeshcomSettings.MyCallsign"/> in that case).
    /// </summary>
    public NodeProfile? ResolveNodeByIp(IPAddress remoteAddress)
    {
        var nodes = _settingsMonitor.CurrentValue.Nodes;
        if (nodes.Count == 0) return null;

        // Normalize IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.243 → 192.168.1.243)
        // so that UdpClient results on dual-stack sockets match NodeProfile.DeviceIp entries.
        var normalized = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : remoteAddress;

        foreach (var node in nodes)
        {
            if (IPAddress.TryParse(node.DeviceIp, out var nodeAddr) &&
                nodeAddr.Equals(normalized))
                return node;
        }
        return null;
    }

    /// <summary>Returns the own callsign for a node identified by its <see cref="NodeProfile.Id"/>.</summary>
    public string GetCallsignForNode(Guid? nodeId)
    {
        var s = _settingsMonitor.CurrentValue;
        if (nodeId is null || nodeId == Guid.Empty) return s.MyCallsign;
        var node = s.Nodes.FirstOrDefault(n => n.Id == nodeId);
        return node?.Callsign ?? s.MyCallsign;
    }

    /// <summary>
    /// Returns the own callsign that matches the node identified by <paramref name="remoteAddress"/>.
    /// Falls back to <see cref="MeshcomSettings.MyCallsign"/> when:
    /// <list type="bullet">
    ///   <item>no node list is configured (legacy single-node mode), or</item>
    ///   <item>the remote IP does not match any configured node.</item>
    /// </list>
    /// </summary>
    public string GetCallsignForIp(IPAddress remoteAddress)
    {
        var s = _settingsMonitor.CurrentValue;
        return ResolveNodeByIp(remoteAddress)?.Callsign ?? s.MyCallsign;
    }

    // ── Connection helpers ───────────────────────────────────────────────

    /// <summary>
    /// Returns the effective connection parameters for the primary node,
    /// falling back to the top-level legacy settings when no node list is configured.
    /// </summary>
    public (string ListenIp, int ListenPort, string DeviceIp, int DevicePort) GetPrimaryConnection()
    {
        var s = _settingsMonitor.CurrentValue;
        var primary = PrimaryNode;
        return primary is not null
            ? (primary.ListenIp, primary.ListenPort, primary.DeviceIp, primary.DevicePort)
            : (s.ListenIp, s.ListenPort, s.DeviceIp, s.DevicePort);
    }

    /// <summary>Returns the own callsign for the primary node.</summary>
    public string GetPrimaryCallsign()
    {
        var s = _settingsMonitor.CurrentValue;
        return PrimaryNode?.Callsign ?? s.MyCallsign;
    }

    /// <summary>Returns the own callsign for the currently selected (chat-view) node.</summary>
    public string GetSelectedCallsign()
    {
        var s = _settingsMonitor.CurrentValue;
        return SelectedNode?.Callsign ?? s.MyCallsign;
    }

    /// <summary>
    /// Returns the effective connection parameters for the selected (chat-view) node,
    /// falling back to the legacy settings when in single-node mode.
    /// </summary>
    public (string ListenIp, int ListenPort, string DeviceIp, int DevicePort) GetSelectedConnection()
    {
        var s = _settingsMonitor.CurrentValue;
        var node = SelectedNode;
        return node is not null
            ? (node.ListenIp, node.ListenPort, node.DeviceIp, node.DevicePort)
            : (s.ListenIp, s.ListenPort, s.DeviceIp, s.DevicePort);
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private void EnsureSelectedNodeValid()
    {
        if (_selectedNodeId is null) return;

        var stillExists = _settingsMonitor.CurrentValue.Nodes.Any(n => n.Id == _selectedNodeId);
        if (!stillExists)
        {
            _selectedNodeId = null;
            OnSelectedNodeChanged?.Invoke();
        }
    }
}
