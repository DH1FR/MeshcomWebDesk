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
    private const int    RadialCount   = 36;     // every 10° → 36 directions
    private const double NearRangeKm   = 50.0;   // near field: high density sampling
    private const int    NearSteps     = 20;     // every 2.5 km up to 50 km
    private const double FarRangeKm    = 300.0;  // far field: coarser sampling
    private const int    FarSteps      = 10;     // every 25 km from 50–300 km
    private const double MinBlockDistKm = 5.0;   // ignore terrain obstructions closer than this
    private const double AtmRefraction = 1.33;   // k-factor standard atmosphere
    private const double FreqMHz       = 433.0;  // LoRa 433 MHz for Fresnel zone
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

            // Build sample points: dense near field (every 2.5 km up to 50 km)
            // + coarse far field (every 25 km from 50–300 km)
            var allPoints = new List<(int radial, int step, double lat, double lon, double distKm)>(
                RadialCount * (NearSteps + FarSteps));

            for (var r = 0; r < RadialCount; r++)
            {
                var bearing = r * (360.0 / RadialCount);
                // Near field
                for (var s = 1; s <= NearSteps; s++)
                {
                    var distKm     = NearRangeKm * s / NearSteps;
                    var (lat, lon) = DestinationPoint(ownLat, ownLon, bearing, distKm);
                    allPoints.Add((r, s - 1, lat, lon, distKm));
                }
                // Far field
                for (var s = 1; s <= FarSteps; s++)
                {
                    var distKm     = NearRangeKm + (FarRangeKm - NearRangeKm) * s / FarSteps;
                    var (lat, lon) = DestinationPoint(ownLat, ownLon, bearing, distKm);
                    allPoints.Add((r, NearSteps + s - 1, lat, lon, distKm));
                }
            }

            // Fetch elevations in batches of 100 (API limit)
            var elevations = await FetchElevationsBatchedAsync(allPoints, ct);

            // ── LOS scan per radial ───────────────────────────────────────
            // Correct algorithm:
            //   - maxAngleDeg is initialised from the FIRST sample point (the baseline)
            //   - Each subsequent point is only a blocker if it rises ABOVE that baseline
            //   - On flat terrain the earth-bulge formula makes angles drop with distance
            //     → radio horizon is reached naturally without any artificial km cap
            var stepsPerRadial = NearSteps + FarSteps;
            var polygon        = new List<double[]>(RadialCount);

            for (var r = 0; r < RadialCount; r++)
            {
                var bearing      = r * (360.0 / RadialCount);
                var maxHorizon   = double.NegativeInfinity;
                var losReachKm   = FarRangeKm;
                var blocked      = false;

                for (var s = 0; s < stepsPerRadial; s++)
                {
                    var idx = r * stepsPerRadial + s;
                    if (idx >= elevations.Count) break;

                    var distKm    = allPoints[idx].distKm;
                    var elevM     = elevations[idx] ?? 0.0;
                    var Re        = EarthRadiusKm * AtmRefraction;
                    var bulgeM    = (distKm * distKm * 1_000_000.0) / (2.0 * Re * 1000.0);
                    var effElevM  = elevM - bulgeM;
                    var fresnelM  = 17.3 * Math.Sqrt(distKm / FreqMHz);
                    var obstacleM = effElevM + 0.6 * fresnelM;
                    var angleDeg  = Math.Atan2(obstacleM - ownTotalM, distKm * 1000.0) * 180.0 / Math.PI;

                    if (angleDeg > maxHorizon)
                    {
                        // This point raises the horizon
                        maxHorizon = angleDeg;

                        // Only count as real blockage if beyond MinBlockDistKm.
                        // Nearby terrain (< 5 km) is tracked for horizon angle
                        // but doesn't block: LoRa 433 MHz diffracts over small
                        // nearby hills thanks to long wavelength (~0.7 m).
                        if (!blocked && distKm >= MinBlockDistKm)
                        {
                            losReachKm = distKm;
                            blocked    = true;
                        }
                    }
                    else if (blocked)
                    {
                        // Behind a real blocker and terrain doesn't rise further
                        break;
                    }

                    // If not yet blocked, this point is reachable
                    if (!blocked)
                        losReachKm = distKm;
                }

                var (pLat, pLon) = DestinationPoint(ownLat, ownLon, bearing, losReachKm);
                polygon.Add([pLat, pLon]);
                _logger.LogDebug("ElevationService: {B}° → {R:F1} km (blocked={Bl})",
                    bearing, losReachKm, blocked);
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
