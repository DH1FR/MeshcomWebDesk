using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Retrieves elevation profiles from the free OpenTopoData API (SRTM 90 m)
/// and performs Line-of-Sight calculations for coverage prediction.
/// No API key required. Rate limit: 1 request/second, 100 points/request.
/// </summary>
public sealed class ElevationService
{
    private const string ApiBase       = "https://api.opentopodata.org/v1/srtm90m";
    private const int    RadialCount   = 36;    // every 10° → 36 directions
    private const int    ProfileSteps  = 16;    // sample points per radial
    private const double MaxRangeKm    = 250.0; // upper bound; earth curvature limits flat terrain naturally
    private const double AtmRefraction = 1.33;  // k-factor for standard atmosphere
    private const double EarthRadiusKm = 6371.0;

    private readonly HttpClient              _http;
    private readonly ILogger<ElevationService> _logger;

    public ElevationService(ILogger<ElevationService> logger)
    {
        _logger = logger;
        _http   = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a LOS-based coverage prediction polygon around the given position.
    /// Returns a list of (lat, lon) polygon vertices ordered by bearing.
    /// Returns empty list on failure.
    /// </summary>
    public async Task<List<double[]>> GetCoveragePolygonAsync(
        double ownLat, double ownLon,
        double antennaHeightM = 2.0,
        CancellationToken ct  = default)
    {
        try
        {
            var ownElevation = await GetSingleElevationAsync(ownLat, ownLon, ct);
            if (ownElevation is null)
            {
                _logger.LogWarning("ElevationService: failed to get own elevation at {Lat},{Lon}", ownLat, ownLon);
                return [];
            }

            var ownTotalM = ownElevation.Value + antennaHeightM;
            _logger.LogInformation("ElevationService: own elevation={E} m, antenna AGL={A} m → total={T} m",
                ownElevation.Value, antennaHeightM, ownTotalM);

            // Build all sample points for all radials
            var allPoints = new List<(int radial, int step, double lat, double lon, double distKm)>(
                RadialCount * ProfileSteps);

            for (var r = 0; r < RadialCount; r++)
            {
                var bearing = r * (360.0 / RadialCount);
                for (var s = 1; s <= ProfileSteps; s++)
                {
                    var distKm = MaxRangeKm * s / ProfileSteps;
                    var (lat, lon) = DestinationPoint(ownLat, ownLon, bearing, distKm);
                    allPoints.Add((r, s, lat, lon, distKm));
                }
            }

            // Fetch elevations in batches of 100 (API limit)
            var elevations = await FetchElevationsBatchedAsync(allPoints, ct);

            // Per radial: walk outward using the "highest horizon angle" method.
            // maxAngleDeg tracks the steepest upward angle seen so far from the antenna.
            // As long as the next terrain point is BELOW this angle, LOS is still clear.
            // The moment a terrain point rises ABOVE maxAngleDeg, it blocks the view
            // → that is the LOS limit for this radial.
            //
            // On flat terrain the earth-bulge formula naturally limits the range because
            // the effective terrain height drops below the radio horizon angle.
            var polygon = new List<double[]>(RadialCount);

            for (var r = 0; r < RadialCount; r++)
            {
                var bearing     = r * (360.0 / RadialCount);
                // Start with a very negative angle so the first terrain point
                // can only block if it is truly upward from the antenna.
                // Using the initial downward look-angle to the horizon.
                var maxAngleDeg = -90.0;  // running horizon angle (degrees above horizontal)
                var losReachKm  = MaxRangeKm;  // default = radio horizon if never blocked

                for (var s = 0; s < ProfileSteps; s++)
                {
                    var idx = r * ProfileSteps + s;
                    if (idx >= elevations.Count) break;

                    var distKm   = allPoints[idx].distKm;
                    var elevM    = elevations[idx] ?? 0.0;

                    // Effective earth radius with atmospheric refraction (k-factor)
                    var Re       = EarthRadiusKm * AtmRefraction;

                    // Earth-bulge: how far the terrain sinks below the straight LOS line
                    var bulgeM   = (distKm * distKm * 1_000_000.0) / (2.0 * Re * 1000.0);
                    var effElevM = elevM - bulgeM;

                    // Elevation angle from antenna tip to this terrain point
                    var angleDeg = Math.Atan2(effElevM - ownTotalM, distKm * 1000.0) * 180.0 / Math.PI;

                    if (angleDeg > maxAngleDeg)
                    {
                        // Terrain rises above running horizon → LOS blocked at this distance
                        maxAngleDeg = angleDeg;
                        losReachKm  = distKm;
                        // Do NOT break here – a closer obstruction earlier in the scan
                        // may have been missed; we take the NEAREST blocking point.
                        // Actually we scan outward so once blocked we stop.
                        break;
                    }
                    // Terrain below horizon → LOS clear, advance outward
                }

                var (pLat, pLon) = DestinationPoint(ownLat, ownLon, bearing, losReachKm);
                polygon.Add([pLat, pLon]);

                _logger.LogDebug("ElevationService: bearing={B}° → reach={R:F1} km", bearing, losReachKm);
            }

            _logger.LogInformation("ElevationService: polygon with {N} vertices computed", polygon.Count);
            return polygon;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ElevationService: GetCoveragePolygonAsync failed");
            return [];
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task<double?> GetSingleElevationAsync(double lat, double lon, CancellationToken ct)
    {
        try
        {
            var url  = $"{ApiBase}?locations={Fmt(lat)},{Fmt(lon)}";
            var resp = await _http.GetFromJsonAsync<OpenTopoResponse>(url, ct);
            var elev = resp?.Results?.FirstOrDefault()?.Elevation;
            _logger.LogDebug("ElevationService: own-point elevation={E}", elev);
            return elev;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ElevationService: GetSingleElevationAsync failed");
            return null;
        }
    }

    private async Task<List<double?>> FetchElevationsBatchedAsync(
        List<(int radial, int step, double lat, double lon, double distKm)> points,
        CancellationToken ct)
    {
        var result     = new List<double?>(points.Count);
        const int batchSize = 100;
        var batchCount = (int)Math.Ceiling((double)points.Count / batchSize);

        for (var i = 0; i < points.Count; i += batchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batch = points.Skip(i).Take(batchSize).ToList();
            var locs  = string.Join("|", batch.Select(p => $"{Fmt(p.lat)},{Fmt(p.lon)}"));

            try
            {
                var url  = $"{ApiBase}?locations={locs}";
                var resp = await _http.GetFromJsonAsync<OpenTopoResponse>(url, ct);
                if (resp?.Results != null && resp.Results.Count == batch.Count)
                {
                    result.AddRange(resp.Results.Select(r => r.Elevation));
                    _logger.LogDebug("ElevationService: batch {B}/{Total} OK ({N} pts)",
                        i / batchSize + 1, batchCount, batch.Count);
                }
                else
                {
                    _logger.LogWarning("ElevationService: batch {B} returned unexpected result count (expected {E}, got {G})",
                        i / batchSize + 1, batch.Count, resp?.Results?.Count ?? -1);
                    result.AddRange(Enumerable.Repeat<double?>(null, batch.Count));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ElevationService: batch {B} failed", i / batchSize + 1);
                result.AddRange(Enumerable.Repeat<double?>(null, batch.Count));
            }

            // Respect rate limit: ≤ 1 request/second
            if (i + batchSize < points.Count)
                await Task.Delay(1200, ct);
        }

        return result;
    }

    /// <summary>Calculates the destination point given start, bearing (°) and distance (km).</summary>
    private static (double lat, double lon) DestinationPoint(
        double lat, double lon, double bearingDeg, double distKm)
    {
        var d    = distKm / EarthRadiusKm;
        var b    = bearingDeg * Math.PI / 180.0;
        var lat1 = lat * Math.PI / 180.0;
        var lon1 = lon * Math.PI / 180.0;

        var lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(d)
                 + Math.Cos(lat1) * Math.Sin(d) * Math.Cos(b));
        var lon2 = lon1 + Math.Atan2(
            Math.Sin(b) * Math.Sin(d) * Math.Cos(lat1),
            Math.Cos(d) - Math.Sin(lat1) * Math.Sin(lat2));

        return (lat2 * 180.0 / Math.PI, lon2 * 180.0 / Math.PI);
    }

    private static string Fmt(double v) =>
        v.ToString("F5", System.Globalization.CultureInfo.InvariantCulture);

    // ── JSON models ───────────────────────────────────────────────────────

    private sealed class OpenTopoResponse
    {
        [JsonPropertyName("results")]
        public List<ElevationResult>? Results { get; set; }
    }

    private sealed class ElevationResult
    {
        [JsonPropertyName("elevation")]
        public double? Elevation { get; set; }
    }
}
