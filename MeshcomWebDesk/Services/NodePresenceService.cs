using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>Reachability estimate for a callsign-SSID, shown as a dot on its chat tab.</summary>
public enum PresenceLevel
{
    /// <summary>Not seen recently, or not listed by any server.</summary>
    Offline,
    /// <summary>Last activity is older than the "fresh" threshold but within the "stale" one.</summary>
    Uncertain,
    /// <summary>Recent activity: probably reachable.</summary>
    Online,
}

/// <param name="Level">Combined estimate from server and local data.</param>
/// <param name="ServerLastSeen">LASTTIME from the dashboard (UTC), null when not listed.</param>
/// <param name="ViaGateway">Gateway the server received the node through, if any.</param>
/// <param name="DirectLastHeard">When this station last received the node directly by radio (UTC), else null.</param>
/// <param name="DirectRssi">RSSI of the last direct reception, if known.</param>
public sealed record PresenceInfo(
    PresenceLevel Level,
    DateTime? ServerLastSeen,
    string? ViaGateway,
    DateTime? DirectLastHeard,
    int? DirectRssi);

/// <summary>
/// Periodically reads the <c>nodes.html</c> page of the configured MeshCom dashboard(s)
/// (same servers as <see cref="GatewayService"/>) and keeps the last-seen time of those
/// callsign-SSIDs that currently have an open direct chat tab. Combined with the local MH list
/// this yields a "probably reachable" estimate per tab.
/// </summary>
public sealed class NodePresenceService : IHostedService, IDisposable
{
    private static readonly TimeSpan RefreshInterval   = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinRefreshSpacing = TimeSpan.FromSeconds(30);

    // A reception counts as "heard directly" for this long.
    private static readonly TimeSpan DirectFreshness = TimeSpan.FromMinutes(45);

    // <tr><td>NR</td><td>2026-10-24   20:33:50</td><td …>VIA GW</td><td …>CALL</td>
    private static readonly Regex RowRegex = new(
        @"<tr><td>\d+</td><td>(?<t>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})</td><td[^>]*>(?<gw>[^<]*)</td><td[^>]*>(?<call>[^<]*)</td>",
        RegexOptions.Compiled);

    // The dashboard prints server local time (observed: Central European Time).
    private static readonly TimeZoneInfo ServerZone = ResolveServerZone();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ChatService _chat;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<NodePresenceService> _logger;

    private volatile Dictionary<string, (DateTime Utc, string? Gw)> _server = new(StringComparer.OrdinalIgnoreCase);
    private volatile HashSet<string> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedUrls = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private int _refreshing;
    private long _lastRefreshTicks;

    /// <summary>Raised after every refresh so open chat pages can repaint.</summary>
    public event Action? Changed;

    public NodePresenceService(IHttpClientFactory httpClientFactory, ChatService chat,
        IOptionsMonitor<MeshcomSettings> settings, ILogger<NodePresenceService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _chat              = chat;
        _settings          = settings;
        _logger            = logger;
    }

    public bool Enabled => _settings.CurrentValue.PresenceEnabled;

    public Task StartAsync(CancellationToken ct)
    {
        _timer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.FromSeconds(20), RefreshInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the chat page with the calls of its open tabs. Triggers an early (rate-limited)
    /// refresh when a tab was opened that the last refresh did not know about.
    /// </summary>
    public void NotifyTabs(IEnumerable<string> calls)
    {
        if (!Enabled) return;
        var known = _tracked;
        if (!calls.Any(c => !known.Contains(c))) return;
        if (DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRefreshTicks) < MinRefreshSpacing.Ticks) return;
        _ = RefreshAsync();
    }

    /// <summary>
    /// Combines server and local knowledge for one callsign-SSID.
    /// <paramref name="heard"/> is the entry from the local MH list (null when never heard);
    /// <paramref name="relaySeen"/> is the last sighting as a relay hop in a packet path.
    /// </summary>
    public PresenceInfo Evaluate(string callsign, HeardStation? heard, (DateTime Time, bool Direct)? relaySeen = null)
    {
        var s     = _settings.CurrentValue;
        var fresh = TimeSpan.FromMinutes(Math.Max(1, s.PresenceFreshMinutes));
        var stale = TimeSpan.FromMinutes(Math.Max(s.PresenceFreshMinutes, s.PresenceStaleMinutes));
        var now   = DateTime.UtcNow;

        DateTime? serverSeen = null;
        string? gw = null;
        if (_server.TryGetValue(callsign, out var e))
        {
            serverSeen = e.Utc;
            gw = string.IsNullOrWhiteSpace(e.Gw) ? null : e.Gw;
        }

        // HeardStation timestamps are local time (message.Timestamp).
        DateTime? localSeen = heard is null ? null : heard.LastHeard.ToUniversalTime();
        DateTime? direct = null;
        int? rssi = null;
        if (heard is not null
            && heard.HopCount == 0
            && !string.Equals(heard.LastSrcType, "udp", StringComparison.OrdinalIgnoreCase)
            && now - localSeen!.Value <= DirectFreshness)
        {
            direct = localSeen;
            rssi   = heard.LastRssi;
        }

        // Seen only as a relay hop (not an originator): counts as activity; the last hop of a
        // path is the station our node received from, i.e. heard directly.
        if (relaySeen is { } rs)
        {
            var rsUtc = rs.Time.ToUniversalTime();
            if (localSeen is null || rsUtc > localSeen) localSeen = rsUtc;
            if (rs.Direct && now - rsUtc <= DirectFreshness && (direct is null || rsUtc > direct))
                direct = rsUtc;
        }

        var latest = serverSeen is null ? localSeen
                   : localSeen is null  ? serverSeen
                   : serverSeen > localSeen ? serverSeen : localSeen;

        var level = latest is null ? PresenceLevel.Offline
                  : now - latest.Value <= fresh ? PresenceLevel.Online
                  : now - latest.Value <= stale ? PresenceLevel.Uncertain
                  : PresenceLevel.Offline;

        return new PresenceInfo(level, serverSeen, gw, direct, rssi);
    }

    // ── Refresh ──────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        try
        {
            Interlocked.Exchange(ref _lastRefreshTicks, DateTime.UtcNow.Ticks);
            var settings = _settings.CurrentValue;
            if (!settings.PresenceEnabled) return;

            var tracked = new HashSet<string>(_chat.GetOpenDirectCalls(), StringComparer.OrdinalIgnoreCase);
            _tracked = tracked;
            if (tracked.Count == 0) return;

            var result = new Dictionary<string, (DateTime Utc, string? Gw)>(StringComparer.OrdinalIgnoreCase);
            foreach (var url in GatewayService.ResolveUrls(settings))
            {
                var nodesUrl = ToNodesUrl(url);
                if (nodesUrl is not null)
                    await FetchIntoAsync(nodesUrl, tracked, result);
            }

            _server = result;
            _logger.LogDebug("NodePresenceService: {Found}/{Tracked} tracked calls found on server.",
                result.Count, tracked.Count);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NodePresenceService: refresh failed.");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private static string? ToNodesUrl(string gatewayUrl) =>
        gatewayUrl.Contains("rakgw.html", StringComparison.OrdinalIgnoreCase)
            ? Regex.Replace(gatewayUrl, @"rakgw\.html", "nodes.html", RegexOptions.IgnoreCase)
            : null;

    private async Task FetchIntoAsync(string url, HashSet<string> tracked,
        Dictionary<string, (DateTime Utc, string? Gw)> target)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("MeshcomGateway");
            var html = await client.GetStringAsync(url);
            foreach (Match m in RowRegex.Matches(html))
            {
                var call = WebUtility.HtmlDecode(m.Groups["call"].Value).Trim();
                if (!tracked.Contains(call)) continue;
                if (!DateTime.TryParse(Regex.Replace(m.Groups["t"].Value, @"\s+", " "),
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                    continue;

                var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ServerZone);
                var gw  = WebUtility.HtmlDecode(m.Groups["gw"].Value).Trim();

                // Several servers may list the same node: keep the freshest.
                if (!target.TryGetValue(call, out var old) || utc > old.Utc)
                    target[call] = (utc, gw);
            }
            lock (_warnedUrls) _warnedUrls.Remove(url);
        }
        catch (Exception ex)
        {
            // Not every dashboard serves nodes.html (e.g. DL returns 404): warn once per URL.
            bool first;
            lock (_warnedUrls) first = _warnedUrls.Add(url);
            if (first)
                _logger.LogWarning("NodePresenceService: could not fetch {Url}: {Message}", url, ex.Message);
        }
    }

    private static TimeZoneInfo ResolveServerZone()
    {
        foreach (var id in new[] { "Europe/Vienna", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        }
        return TimeZoneInfo.Utc;
    }

    public void Dispose() => _timer?.Dispose();
}
