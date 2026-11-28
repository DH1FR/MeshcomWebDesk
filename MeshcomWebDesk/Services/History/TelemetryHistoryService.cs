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
