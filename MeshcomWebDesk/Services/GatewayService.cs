using System.Collections.Frozen;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Periodically fetches the list of active MeshCom gateway stations from the public
/// dashboard(s) and makes it available as a frozen set of upper-cased callsigns.
/// The server source (OE, DL, or both) is configurable via <see cref="MeshcomSettings.GatewayServer"/>.
/// </summary>
public sealed class GatewayService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    // Matches the callsign cell – always has bgcolor="#00FF66" and contains "CALL-N (nn)"
    // e.g. <td bgcolor="#00FF66">DH1FR-2 (74)</td>
    private static readonly Regex CallsignRegex = new(
        @"<td\s+bgcolor=""#00FF66"">([A-Z0-9]+-\d+)\s*\(\d+\)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string GatewayUrlOe = "https://meshcom.oevsv.at/gateways.html";
    private const string GatewayUrlDl = "http://meshcom.hamnet.network/meshcom/gateways.html";

    private readonly IHttpClientFactory  _httpClientFactory;
    private readonly ILogger<GatewayService> _logger;
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private FrozenSet<string> _gateways = FrozenSet<string>.Empty;
    private Timer?  _timer;

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
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    // ── Internal ─────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        try
        {
            var server = _settings.CurrentValue.GatewayServer?.ToLowerInvariant() ?? "oe";
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (server == "dl")
                await FetchIntoAsync(GatewayUrlDl, set);
            else
                await FetchIntoAsync(GatewayUrlOe, set);

            _gateways = set.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            _logger.LogDebug("GatewayService: {Count} gateways loaded (server={Server}).", _gateways.Count, server);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GatewayService: failed to refresh gateway list.");
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
        if (_timer is not null)
            await _timer.DisposeAsync();
    }
}
