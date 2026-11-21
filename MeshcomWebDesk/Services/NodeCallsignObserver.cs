namespace MeshcomWebDesk.Services;

/// <summary>
/// Passively records which sender callsigns a node reports in its <c>src_type:"node"</c> packets
/// (own positions, telemetry, message echoes). The connection check uses this to tell whether the
/// callsign configured in WebDesk is the one the node really uses – it never changes any setting.
/// </summary>
public sealed class NodeCallsignObserver
{
    /// <summary>
    /// Observed packets of one node: total count, per-sender counts and – when the node echoed one of WebDesk's own
    /// messages under a different callsign – the callsign it used (<paramref name="EchoCallsign"/>).
    /// </summary>
    public sealed record Snapshot(int Total, IReadOnlyDictionary<string, int> Counts, string? EchoCallsign = null);

    private sealed class Entry
    {
        public int Total;
        public readonly Dictionary<string, int> Counts = new(StringComparer.OrdinalIgnoreCase);
        public string? EchoCallsign;
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    /// <summary>Records one <c>src_type:"node"</c> packet (not an ACK) from the given node.</summary>
    /// <param name="nodeId">Node profile id, or <see cref="Guid.Empty"/> in legacy single-node mode.</param>
    /// <param name="sourceCallsign">The <c>src</c> callsign of the packet.</param>
    public void Record(Guid nodeId, string? sourceCallsign)
    {
        if (string.IsNullOrWhiteSpace(sourceCallsign)) return;
        lock (_lock)
        {
            if (!_entries.TryGetValue(nodeId, out var e))
                _entries[nodeId] = e = new Entry();
            e.Total++;
            e.Counts[sourceCallsign.Trim()] = e.Counts.GetValueOrDefault(sourceCallsign.Trim()) + 1;
        }
    }

    /// <summary>
    /// Records that the node returned a message WebDesk had just sent, but under <paramref name="echoCallsign"/>.
    /// That is certain proof of the callsign the node really uses.
    /// </summary>
    public void RecordOwnEcho(Guid nodeId, string echoCallsign)
    {
        if (string.IsNullOrWhiteSpace(echoCallsign)) return;
        lock (_lock)
        {
            if (!_entries.TryGetValue(nodeId, out var e))
                _entries[nodeId] = e = new Entry();
            e.EchoCallsign = echoCallsign.Trim();
        }
    }

    /// <summary>Forgets a recorded echo (the callsign was corrected).</summary>
    public void ClearOwnEcho(Guid nodeId)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(nodeId, out var e)) e.EchoCallsign = null;
        }
    }

    /// <summary>Returns what has been observed for the node so far (empty when nothing).</summary>
    public Snapshot Get(Guid nodeId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(nodeId, out var e)
                ? new Snapshot(e.Total, new Dictionary<string, int>(e.Counts, StringComparer.OrdinalIgnoreCase), e.EchoCallsign)
                : new Snapshot(0, new Dictionary<string, int>());
        }
    }
}
