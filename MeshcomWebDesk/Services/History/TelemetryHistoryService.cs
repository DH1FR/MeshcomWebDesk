using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.History;

/// <summary>
/// Single entry point for the charts: reads from the configured database (MySQL / InfluxDB) and falls back to the
/// built-in local history when no database is configured or the database cannot be reached.
/// </summary>
public sealed class TelemetryHistoryService
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly MySqlTelemetryHistory  _mysql;
    private readonly InfluxTelemetryHistory _influx;
    private readonly LocalTelemetryHistory  _local;
    private readonly ILogger<TelemetryHistoryService> _logger;

    public TelemetryHistoryService(IOptionsMonitor<MeshcomSettings> settings, MySqlTelemetryHistory mysql,
        InfluxTelemetryHistory influx, LocalTelemetryHistory local, ILogger<TelemetryHistoryService> logger)
    {
        _settings = settings; _mysql = mysql; _influx = influx; _local = local; _logger = logger;
    }

    /// <summary>Longest range the local history can serve.</summary>
    public static TimeSpan LocalRange => LocalTelemetryHistory.Keep;

    private (ITelemetryHistory Reader, HistorySource Source) Pick() => _settings.CurrentValue.Database.Provider switch
    {
        DatabaseSettings.ProviderMySql    => (_mysql,  HistorySource.MySql),
        DatabaseSettings.ProviderInfluxDb => (_influx, HistorySource.InfluxDb),
        _                                  => (_local,  HistorySource.Local)
    };

    public async Task<SeriesResult> GetSeriesAsync(string callsign, HistoryMetric metric, DateTime from, DateTime to,
        int maxPoints = 200, CancellationToken ct = default)
    {
        var (reader, source) = Pick();
        try
        {
            return new SeriesResult(await reader.GetSeriesAsync(callsign, metric, from, to, maxPoints, ct), source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && source != HistorySource.Local)
        {
            _logger.LogWarning("Telemetry history: {Source} not readable ({Message}) – using local history", source, ex.Message);
            return new SeriesResult(await _local.GetSeriesAsync(callsign, metric, from, to, maxPoints, ct),
                                    HistorySource.Local, ex.Message);
        }
    }

    private static readonly HistoryMetric[] SparkMetrics =
        [HistoryMetric.Temp1, HistoryMetric.Humidity, HistoryMetric.Pressure, HistoryMetric.Battery, HistoryMetric.Rssi, HistoryMetric.Snr];
    private static readonly TimeSpan SparkCacheTime = TimeSpan.FromSeconds(60);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, Dictionary<HistoryMetric, SeriesResult> Data)> _sparkCache = new();

    /// <summary>All sparkline metrics of one station (about 48 points each). Cached for a minute so hovering does not hit the database repeatedly.</summary>
    public async Task<Dictionary<HistoryMetric, SeriesResult>> GetSparklinesAsync(string callsign, DateTime from, DateTime to,
        CancellationToken ct = default)
    {
        var key = callsign.ToUpperInvariant();
        if (_sparkCache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < SparkCacheTime)
            return hit.Data;

        var results = await Task.WhenAll(SparkMetrics.Select(m => GetSeriesAsync(callsign, m, from, to, 48, ct)));
        var data = SparkMetrics.Zip(results).ToDictionary(x => x.First, x => x.Second);

        if (_sparkCache.Count > 200) _sparkCache.Clear();     // trivial bound
        _sparkCache[key] = (DateTime.UtcNow, data);
        return data;
    }

    public async Task<StationsResult> GetStationsAsync(HistoryMetric metric, DateTime from, int limit = 100,
        CancellationToken ct = default)
    {
        var (reader, source) = Pick();
        try
        {
            return new StationsResult(await reader.GetStationsAsync(metric, from, limit, ct), source);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && source != HistorySource.Local)
        {
            _logger.LogWarning("Telemetry history: {Source} not readable ({Message}) – using local history", source, ex.Message);
            return new StationsResult(await _local.GetStationsAsync(metric, from, limit, ct), HistorySource.Local, ex.Message);
        }
    }
}
