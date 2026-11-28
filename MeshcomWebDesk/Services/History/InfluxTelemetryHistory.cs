using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.History;

/// <summary>Reads the history from the bucket written by <c>InfluxDbMonitorSink</c> (Flux queries).</summary>
public sealed class InfluxTelemetryHistory : ITelemetryHistory
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly IOptionsMonitor<MeshcomSettings> _settings;

    public InfluxTelemetryHistory(IOptionsMonitor<MeshcomSettings> settings) => _settings = settings;

    private static string Iso(DateTime local) =>
        local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<SeriesPoint>> GetSeriesAsync(string callsign, HistoryMetric metric,
        DateTime from, DateTime to, int maxPoints, CancellationToken ct = default)
    {
        if (!HistoryHelper.IsValidCallsign(callsign)) return [];
        var db     = _settings.CurrentValue.Database;
        var bucket = HistoryHelper.BucketSeconds(from, to, maxPoints);

        var flux = $"""
            from(bucket: "{Esc(db.InfluxBucket)}")
              |> range(start: {Iso(from)}, stop: {Iso(to)})
              |> filter(fn: (r) => r._measurement == "meshcom_monitor" and r.from == "{callsign}" and r._field == "{HistoryHelper.Column(metric)}")
              |> aggregateWindow(every: {bucket}s, fn: mean, createEmpty: false)
              |> keep(columns: ["_time", "_value"])
            """;

        var rows = await QueryAsync(db, flux, ct);
        var list = new List<SeriesPoint>(rows.Count);
        foreach (var r in rows)
            if (r.Length >= 2 && DateTime.TryParse(r[0], CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) &&
                double.TryParse(r[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                // aggregateWindow stamps the window END – shift to the window centre like the MySQL reader
                list.Add(new SeriesPoint(t.ToLocalTime().AddSeconds(-bucket / 2.0), v));
        return list;
    }

    public async Task<IReadOnlyList<StationInfo>> GetStationsAsync(HistoryMetric metric, DateTime from, int limit,
        CancellationToken ct = default)
    {
        var db = _settings.CurrentValue.Database;
        var flux = $"""
            from(bucket: "{Esc(db.InfluxBucket)}")
              |> range(start: {Iso(from)})
              |> filter(fn: (r) => r._measurement == "meshcom_monitor" and r._field == "{HistoryHelper.Column(metric)}")
              |> group(columns: ["from"])
              |> count()
              |> group()
              |> sort(columns: ["_value"], desc: true)
              |> limit(n: {Math.Clamp(limit, 1, 500)})
              |> keep(columns: ["from", "_value"])
            """;

        var rows = await QueryAsync(db, flux, ct);
        var list = new List<StationInfo>(rows.Count);
        foreach (var r in rows)
            if (r.Length >= 2 && int.TryParse(r[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                list.Add(new StationInfo(r[0], n));
        return list;
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Runs a Flux query and returns the data rows of the CSV result (annotation/header rows skipped).
    /// Columns are returned in the order of the query's <c>keep()</c>, without the leading result/table columns.</summary>
    private static async Task<List<string[]>> QueryAsync(DatabaseSettings db, string flux, CancellationToken ct)
    {
        var url = $"{db.InfluxUrl.TrimEnd('/')}/api/v2/query?org={Uri.EscapeDataString(db.InfluxOrg)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(flux, Encoding.UTF8, "application/vnd.flux")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Token", db.InfluxToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/csv"));

        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"InfluxDB query failed (HTTP {(int)resp.StatusCode})");

        var rows = new List<string[]>();
        string[]? header = null;
        foreach (var line in body.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length == 0) { header = null; continue; }      // blank line separates tables
            if (l[0] == '#') continue;                           // annotation rows
            var cols = l.Split(',');
            if (header is null) { header = cols; continue; }     // header row of the table
            // data row: drop the leading empty column, "result" and "table"
            var names = new List<string>(); var vals = new List<string>();
            for (var i = 3; i < cols.Length && i < header.Length; i++) { names.Add(header[i]); vals.Add(cols[i]); }
            rows.Add(Order(names, vals));
        }
        return rows;
    }

    // Query results always list _time/from first, then _value; normalise to [key, value].
    private static string[] Order(List<string> names, List<string> vals)
    {
        var iv = names.IndexOf("_value");
        var ik = names.FindIndex(n => n is "_time" or "from");
        return iv >= 0 && ik >= 0 ? [vals[ik], vals[iv]] : vals.ToArray();
    }
}
