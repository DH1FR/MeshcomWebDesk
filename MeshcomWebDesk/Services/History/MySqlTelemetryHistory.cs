using System.Globalization;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace MeshcomWebDesk.Services.History;

/// <summary>Reads the history from the monitor table written by <c>MySqlMonitorSink</c>.</summary>
public sealed class MySqlTelemetryHistory : ITelemetryHistory
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;

    public MySqlTelemetryHistory(IOptionsMonitor<MeshcomSettings> settings) => _settings = settings;

    public async Task<IReadOnlyList<SeriesPoint>> GetSeriesAsync(string callsign, HistoryMetric metric,
        DateTime from, DateTime to, int maxPoints, CancellationToken ct = default)
    {
        var db     = _settings.CurrentValue.Database;
        var bucket = HistoryHelper.BucketSeconds(from, to, maxPoints);
        var col    = HistoryHelper.Column(metric);

        // Bucket index = whole buckets since 'from'; avoids time-zone conversions in SQL.
        var sql = $"""
            SELECT FLOOR(TIMESTAMPDIFF(SECOND, @from, timestamp) / @b) AS idx, AVG(`{col}`) AS v
            FROM `{db.MySqlTableName.Replace("`", "")}`
            WHERE from_call = @call AND timestamp >= @from AND timestamp <= @to AND `{col}` IS NOT NULL
            GROUP BY idx ORDER BY idx
            """;

        await using var conn = new MySqlConnection(db.MySqlConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@call", callsign);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@to", to);
        cmd.Parameters.AddWithValue("@b", bucket);

        var list = new List<SeriesPoint>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var idx = Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture);
            var v   = Convert.ToDouble(r.GetValue(1), CultureInfo.InvariantCulture);
            list.Add(new SeriesPoint(from.AddSeconds(idx * bucket + bucket / 2.0), v));   // bucket centre
        }
        return list;
    }

    public async Task<IReadOnlyList<StationInfo>> GetStationsAsync(HistoryMetric metric, DateTime from, int limit,
        CancellationToken ct = default)
    {
        var db  = _settings.CurrentValue.Database;
        var col = HistoryHelper.Column(metric);
        var sql = $"""
            SELECT from_call, COUNT(*) AS n
            FROM `{db.MySqlTableName.Replace("`", "")}`
            WHERE timestamp >= @from AND `{col}` IS NOT NULL
            GROUP BY from_call ORDER BY n DESC LIMIT @limit
            """;

        await using var conn = new MySqlConnection(db.MySqlConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@from", from);
        cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 500));

        var list = new List<StationInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new StationInfo(r.GetString(0), Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture)));
        return list;
    }
}
