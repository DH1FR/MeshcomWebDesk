using System.Text.Json;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.History;

/// <summary>
/// Small built-in history that works without a database: every measurement is averaged into
/// 15-minute buckets per station and metric, kept for <see cref="Keep"/> and saved to
/// <c>telemetry-history.json</c> in the data folder (every 5 minutes and on shutdown).
/// </summary>
public sealed class LocalTelemetryHistory : BackgroundService, ITelemetryHistory
{
    public static readonly TimeSpan BucketLength = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Keep         = TimeSpan.FromDays(7);
    private const string FileName = "telemetry-history.json";

    private static readonly HistoryMetric[] AllMetrics = Enum.GetValues<HistoryMetric>();

    // key = "CALL|Metric", value = buckets ordered by time
    private readonly Dictionary<string, List<Bucket>> _data = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<LocalTelemetryHistory>   _logger;
    private bool _dirty;

    public sealed class Bucket
    {
        public long   Start { get; set; }   // unix seconds (UTC), start of the 15-min window
        public double Sum   { get; set; }
        public int    Count { get; set; }
    }

    public LocalTelemetryHistory(IOptionsMonitor<MeshcomSettings> settings, ILogger<LocalTelemetryHistory> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    private static string Key(string call, HistoryMetric m) => $"{call}|{m}";

    /// <summary>Records all measurements of a monitor message. Cheap; called for every message.</summary>
    public void Record(MeshcomMessage msg)
    {
        if (msg.IsOutgoing || string.IsNullOrEmpty(msg.From) || !HistoryHelper.IsValidCallsign(msg.From)) return;
        var start = Floor(msg.Timestamp);

        lock (_lock)
        {
            foreach (var m in AllMetrics)
            {
                if (HistoryHelper.Value(msg, m) is not { } v || double.IsNaN(v)) continue;
                var key = Key(msg.From, m);
                if (!_data.TryGetValue(key, out var list)) _data[key] = list = [];
                var last = list.Count > 0 ? list[^1] : null;
                if (last is not null && last.Start == start) { last.Sum += v; last.Count++; }
                else if (last is null || last.Start < start) list.Add(new Bucket { Start = start, Sum = v, Count = 1 });
                else
                {   // late/out-of-order message: find its bucket
                    var b = list.Find(x => x.Start == start);
                    if (b is not null) { b.Sum += v; b.Count++; }
                    else { list.Add(new Bucket { Start = start, Sum = v, Count = 1 }); list.Sort((a, c) => a.Start.CompareTo(c.Start)); }
                }
                _dirty = true;
            }
        }
    }

    private static long Floor(DateTime local)
    {
        var s = new DateTimeOffset(local).ToUnixTimeSeconds();
        var len = (long)BucketLength.TotalSeconds;
        return s - s % len;
    }

    public Task<IReadOnlyList<SeriesPoint>> GetSeriesAsync(string callsign, HistoryMetric metric,
        DateTime from, DateTime to, int maxPoints, CancellationToken ct = default)
    {
        var fromS = new DateTimeOffset(from).ToUnixTimeSeconds();
        var toS   = new DateTimeOffset(to).ToUnixTimeSeconds();
        var step  = Math.Max((long)BucketLength.TotalSeconds, HistoryHelper.BucketSeconds(from, to, maxPoints));
        step -= step % (long)BucketLength.TotalSeconds;          // whole multiples of the stored bucket

        var outPts = new List<SeriesPoint>();
        lock (_lock)
        {
            if (!_data.TryGetValue(Key(callsign, metric), out var list)) return Task.FromResult<IReadOnlyList<SeriesPoint>>(outPts);
            long curIdx = long.MinValue; double sum = 0; int cnt = 0;
            void Flush() { if (cnt > 0) outPts.Add(new SeriesPoint(DateTimeOffset.FromUnixTimeSeconds(fromS + curIdx * step + step / 2).LocalDateTime, sum / cnt)); }
            foreach (var b in list)
            {
                if (b.Start < fromS || b.Start > toS) continue;
                var idx = (b.Start - fromS) / step;
                if (idx != curIdx) { Flush(); curIdx = idx; sum = 0; cnt = 0; }
                sum += b.Sum; cnt += b.Count;          // weighted by sample count
            }
            Flush();
        }
        return Task.FromResult<IReadOnlyList<SeriesPoint>>(outPts);
    }

    public Task<IReadOnlyList<StationInfo>> GetStationsAsync(HistoryMetric metric, DateTime from, int limit,
        CancellationToken ct = default)
    {
        var fromS = new DateTimeOffset(from).ToUnixTimeSeconds();
        var suffix = "|" + metric;
        List<StationInfo> result;
        lock (_lock)
        {
            result = _data
                .Where(kv => kv.Key.EndsWith(suffix, StringComparison.Ordinal))
                .Select(kv => new StationInfo(kv.Key[..^suffix.Length], kv.Value.Where(b => b.Start >= fromS).Sum(b => b.Count)))
                .Where(s => s.Samples > 0)
                .OrderByDescending(s => s.Samples)
                .Take(Math.Clamp(limit, 1, 500))
                .ToList();
        }
        return Task.FromResult<IReadOnlyList<StationInfo>>(result);
    }

    // ── persistence ──────────────────────────────────────────────────────

    private string FilePath => Path.Combine(_settings.CurrentValue.DataPath, FileName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Load();
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                Save();
        }
        catch (OperationCanceledException) { }
        Save();
    }

    private void Prune()
    {
        var cutoff = new DateTimeOffset(DateTime.Now - Keep).ToUnixTimeSeconds();
        foreach (var key in _data.Keys.ToList())
        {
            _data[key].RemoveAll(b => b.Start < cutoff);
            if (_data[key].Count == 0) _data.Remove(key);
        }
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_lock)
            {
                if (!_dirty) return;
                Prune();
                json = JsonSerializer.Serialize(_data);
                _dirty = false;
            }
            Directory.CreateDirectory(_settings.CurrentValue.DataPath);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Telemetry history could not be saved"); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<Bucket>>>(File.ReadAllText(FilePath));
            if (loaded is null) return;
            lock (_lock)
            {
                foreach (var (k, v) in loaded) _data[k] = v;
                Prune();
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Telemetry history could not be loaded"); }
    }
}
