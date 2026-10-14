using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Tracks whether a person is really looking at a chat tab, as opposed to merely having a
/// browser window open. Each Blazor circuit that shows the chat page registers itself as a
/// "viewer" and reports (a) which node/tab it displays, (b) whether the browser tab is visible
/// and (c) how long ago the last mouse/keyboard/touch activity was.
/// A viewer counts as <em>attending</em> while it is visible and its idle time is below
/// <see cref="MqttSettings.UnreadActiveSeconds"/>. Idle time keeps growing between reports, so a
/// frozen or abandoned browser stops counting on its own.
/// </summary>
public sealed class AttentionService
{
    private sealed class Viewer
    {
        public bool      Visible;
        public double    IdleMs;
        public DateTime  ReportedUtc = DateTime.UtcNow;
        public Guid?     NodeId;
        public string?   TabKey;
    }

    private readonly ConcurrentDictionary<Guid, Viewer> _viewers = new();
    private readonly IOptionsMonitor<MeshcomSettings>   _settings;

    public AttentionService(IOptionsMonitor<MeshcomSettings> settings) => _settings = settings;

    /// <summary>
    /// Raised when an attending viewer reports in. Arguments: the node and tab key that viewer shows.
    /// Handlers must be cheap – this fires on every activity report (a few seconds apart at most).
    /// </summary>
    public event Action<Guid?, string>? OnAttended;

    private TimeSpan ActiveWindow =>
        TimeSpan.FromSeconds(Math.Clamp(_settings.CurrentValue.Mqtt.UnreadActiveSeconds, 5, 3600));

    /// <summary>Visibility/idle report coming from the browser of <paramref name="viewerId"/>.</summary>
    public void Report(Guid viewerId, bool visible, double idleMs)
    {
        var v = _viewers.GetOrAdd(viewerId, _ => new Viewer());
        Guid?   nodeId;
        string? tabKey;
        bool    attending;
        lock (v)
        {
            v.Visible     = visible;
            v.IdleMs      = Math.Max(0, idleMs);
            v.ReportedUtc = DateTime.UtcNow;
            nodeId        = v.NodeId;
            tabKey        = v.TabKey;
            attending     = IsAttendingLocked(v, DateTime.UtcNow);
        }
        if (attending && !string.IsNullOrEmpty(tabKey))
            OnAttended?.Invoke(nodeId, tabKey);
    }

    /// <summary>Sets which node/tab the viewer currently displays. Never raises <see cref="OnAttended"/>.</summary>
    public void SetView(Guid viewerId, Guid? nodeId, string? tabKey)
    {
        var v = _viewers.GetOrAdd(viewerId, _ => new Viewer());
        lock (v) { v.NodeId = nodeId; v.TabKey = tabKey; }
    }

    public void Remove(Guid viewerId) => _viewers.TryRemove(viewerId, out _);

    /// <summary>True while the given viewer is visible and recently active.</summary>
    public bool IsAttending(Guid viewerId)
    {
        if (!_viewers.TryGetValue(viewerId, out var v)) return false;
        lock (v) return IsAttendingLocked(v, DateTime.UtcNow);
    }

    /// <summary>
    /// True when at least one attending viewer shows a tab for which <paramref name="matches"/> returns true.
    /// The predicate receives the viewer's raw node id and tab key.
    /// </summary>
    public bool IsAnyoneAttending(Func<Guid?, string, bool> matches)
    {
        var now = DateTime.UtcNow;
        foreach (var v in _viewers.Values)
        {
            lock (v)
            {
                if (!string.IsNullOrEmpty(v.TabKey) && IsAttendingLocked(v, now) && matches(v.NodeId, v.TabKey))
                    return true;
            }
        }
        return false;
    }

    private bool IsAttendingLocked(Viewer v, DateTime nowUtc)
    {
        if (!v.Visible) return false;
        var idle = TimeSpan.FromMilliseconds(v.IdleMs) + (nowUtc - v.ReportedUtc);
        return idle < ActiveWindow;
    }
}
