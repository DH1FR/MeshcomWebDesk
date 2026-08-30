using System.Collections.Frozen;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Periodically fetches the list of active MeshCom gateway stations from one or more
/// public dashboards and makes it available as a frozen set of upper-cased callsigns.
/// The built-in preset source(s) are selected via <see cref="MeshcomSettings.GatewayServer"/>;
/// additional dashboards can be added by the user via <see cref="MeshcomSettings.GatewaySources"/>.
/// </summary>
public sealed class GatewayService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    // Matches the callsign cell – always has bgcolor="#00FF66" and contains "CALL-N (nn)"
    // e.g. <td bgcolor="#00FF66">DH1FR-2 (74)</td>
    // All MeshCom dashboards (OE, DL, IT, …) serve this identical markup on their rakgw.html page.
    private static readonly Regex CallsignRegex = new(
        @"<td\s+bgcolor=""#00FF66"">([A-Z0-9]+-\d+)\s*\(\d+\)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Built-in gateway dashboards, keyed by the short code stored in
    /// <see cref="MeshcomSettings.GatewayServer"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PresetUrls =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["oe"] = "https://meshcom.oevsv.at/rakgw.html",
            ["dl"] = "http://meshcom.hamnet.network/meshcom/rakgw.html",
            ["it"] = "https://meshcom.dig-italia.it/rakgw.html",
        };

    private readonly IHttpClientFactory  _httpClientFactory;
    private readonly ILogger<GatewayService> _logger;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private FrozenSet<string> _gateways = FrozenSet<string>.Empty;
    private Timer?  _timer;
    private IDisposable? _settingsChangeSub;
    private int _refreshing;      // 0 = idle, 1 = a refresh is in progress
    private int _refreshPending;  // set when a change arrives mid-refresh; re-runs afterwards

    public GatewayService(IHttpClientFactory httpClientFactory, ILogger<GatewayService> logger,
        IOptionsMonitor<MeshcomSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
        _settings          = settings;
    }

    // ── Public API ───────────────────────────────────────────────────────

    /// <summary>Returns true when the callsign (case-insensitive) is a known gateway.</summary>
    public bool IsGateway(string? callsign)
        => callsign is not null && _gateways.Contains(callsign.ToUpperInvariant());

    /// <summary>Snapshot of all currently known gateway callsigns (upper-cased).</summary>
    public IReadOnlySet<string> KnownGateways => _gateways;

    // ── IHostedService ───────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct)
    {
        // Fetch immediately so the list is ready before the first map render.
        await RefreshAsync();
        _timer = new Timer(async _ => await RefreshAsync(), null,
                           dueTime:  RefreshInterval,
                           period:   RefreshInterval);

        // React to setting changes (source added/removed, preset switched) without
        // waiting up to 15 minutes for the next timer tick.
        _settingsChangeSub = _settings.OnChange(_ => TriggerRefresh());
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _settingsChangeSub?.Dispose();
        _settingsChangeSub = null;
        return Task.CompletedTask;
    }

    // ── Internal ─────────────────────────────────────────────────────────

    /// <summary>Fire-and-forget refresh, used by the settings-change subscription.</summary>
    private void TriggerRefresh() => _ = RefreshAsync();

    /// <summary>
    /// Resolves the effective list of dashboard URLs to scrape from the current settings:
    /// the selected preset(s) plus every enabled user-defined source.
    /// </summary>
    private static IReadOnlyList<string> ResolveUrls(MeshcomSettings settings)
    {
        var urls = new List<string>();

        // GatewayServer may be a single code ("oe"), the legacy "both" alias, "none",
        // or a space/comma separated list ("oe it").
        var raw = settings.GatewayServer?.Trim() ?? "oe";
        var tokens = raw.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.ToLowerInvariant())
                        .ToList();

        if (tokens.Count == 0)
            tokens.Add("oe");

        foreach (var token in tokens)
        {
            if (token is "none")
                continue;
            if (token is "both")
            {
                urls.Add(PresetUrls["oe"]);
                urls.Add(PresetUrls["dl"]);
            }
            else if (PresetUrls.TryGetValue(token, out var url))
            {
                urls.Add(url);
            }
        }

        foreach (var src in settings.GatewaySources)
        {
            if (src.Enabled && !string.IsNullOrWhiteSpace(src.Url))
                urls.Add(src.Url.Trim());
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task RefreshAsync()
    {
        // Coalesce concurrent calls (timer tick colliding with an OnChange notification,
        // or several notifications in quick succession). If a call arrives while a refresh
        // is running, it just marks the result stale and the running refresh loops once more.
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _refreshPending, 0);

                var settings = _settings.CurrentValue;
                var urls = ResolveUrls(settings);
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var url in urls)
                    await FetchIntoAsync(url, set);

                _gateways = set.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                _logger.LogDebug("GatewayService: {Count} gateways loaded from {SourceCount} source(s) [{Urls}].",
                    _gateways.Count, urls.Count, string.Join(", ", urls));
            }
            while (Interlocked.CompareExchange(ref _refreshPending, 0, 1) == 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GatewayService: failed to refresh gateway list.");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private async Task FetchIntoAsync(string url, HashSet<string> target)
    {
        try
        {
            using var client = _httpClientFactory.CreateClient("MeshcomGateway");
            var html = await client.GetStringAsync(url);
            foreach (Match m in CallsignRegex.Matches(html))
                target.Add(m.Groups[1].Value.ToUpperInvariant());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GatewayService: failed to fetch {Url}.", url);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _settingsChangeSub?.Dispose();
        if (_timer is not null)
            await _timer.DisposeAsync();
    }
}
