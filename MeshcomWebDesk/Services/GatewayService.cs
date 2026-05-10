using System.Collections.Frozen;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Periodically fetches the list of active MeshCom gateway stations from the public
/// dashboard at https://meshcom.oevsv.at/gateways.html and makes it available as a
/// frozen set of upper-cased callsigns.
/// </summary>
public sealed class GatewayService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    // Matches a callsign cell in the gateways.html table – e.g. <td>OE3XIR-12</td>
    private static readonly Regex CallsignRegex = new(
        @"<td[^>]*>\s*([A-Z0-9]+-\d+)\s*</td>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string GatewayUrl = "https://meshcom.oevsv.at/gateways.html";

    private readonly IHttpClientFactory  _httpClientFactory;
    private readonly ILogger<GatewayService> _logger;
    private FrozenSet<string> _gateways = FrozenSet<string>.Empty;
    private Timer?  _timer;

    public GatewayService(IHttpClientFactory httpClientFactory, ILogger<GatewayService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
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
            using var client = _httpClientFactory.CreateClient("MeshcomGateway");
            var html = await client.GetStringAsync(GatewayUrl);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in CallsignRegex.Matches(html))
                set.Add(m.Groups[1].Value.ToUpperInvariant());

            _gateways = set.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            _logger.LogDebug("GatewayService: {Count} gateways loaded.", _gateways.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GatewayService: failed to refresh gateway list.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
            await _timer.DisposeAsync();
    }
}
