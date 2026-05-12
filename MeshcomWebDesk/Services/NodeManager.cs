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

    /// <summary>Raised whenever the selected (chat-view) node changes.</summary>
    public event Action? OnSelectedNodeChanged;

    private Guid? _selectedNodeId;

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
