using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.History;

/// <summary>Measurements that can be charted over time.</summary>
public enum HistoryMetric
{
    Temp1,
    Temp2,
    Humidity,
    Pressure,
    Battery,
    Rssi,
    Snr
}

/// <summary>One (time, value) point of a series. <see cref="Time"/> is local time.</summary>
public readonly record struct SeriesPoint(DateTime Time, double Value);

/// <summary>A station that has data for a metric in the requested window.</summary>
public readonly record struct StationInfo(string Callsign, int Samples);

/// <summary>Where a result came from – lets the UI show "from database" / "local history".</summary>
public enum HistorySource { Local, MySql, InfluxDb }

public sealed record SeriesResult(IReadOnlyList<SeriesPoint> Points, HistorySource Source, string? Warning = null);
public sealed record StationsResult(IReadOnlyList<StationInfo> Stations, HistorySource Source, string? Warning = null);

/// <summary>Read access to the measurement history of the stations.</summary>
public interface ITelemetryHistory
{
    /// <summary>Time series of one metric for one station, averaged into at most about <paramref name="maxPoints"/> buckets.</summary>
    Task<IReadOnlyList<SeriesPoint>> GetSeriesAsync(string callsign, HistoryMetric metric,
        DateTime from, DateTime to, int maxPoints, CancellationToken ct = default);

    /// <summary>Stations that have values for the metric since <paramref name="from"/>, most samples first.</summary>
    Task<IReadOnlyList<StationInfo>> GetStationsAsync(HistoryMetric metric, DateTime from, int limit,
        CancellationToken ct = default);
}

internal static class HistoryHelper
{
    /// <summary>Bucket length in seconds so that the range yields at most <paramref name="maxPoints"/> points (min. 60 s).</summary>
    public static int BucketSeconds(DateTime from, DateTime to, int maxPoints)
    {
        var span = Math.Max(1, (to - from).TotalSeconds);
        return (int)Math.Max(60, Math.Ceiling(span / Math.Max(10, maxPoints)));
    }

    /// <summary>Whitelisted column (MySQL) / field (InfluxDB) name of a metric – never user input.</summary>
    public static string Column(HistoryMetric m) => m switch
    {
        HistoryMetric.Temp1    => "temp1",
        HistoryMetric.Temp2    => "temp2",
        HistoryMetric.Humidity => "humidity",
        HistoryMetric.Pressure => "pressure",
        HistoryMetric.Battery  => "battery",
        HistoryMetric.Rssi     => "rssi",
        HistoryMetric.Snr      => "snr",
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };

    public static double? Value(MeshcomMessage msg, HistoryMetric m) => m switch
    {
        HistoryMetric.Temp1    => msg.Temp1,
        HistoryMetric.Temp2    => msg.Temp2,
        HistoryMetric.Humidity => msg.Humidity,
        HistoryMetric.Pressure => msg.Pressure,
        HistoryMetric.Battery  => msg.Battery,
        HistoryMetric.Rssi     => msg.Rssi,
        HistoryMetric.Snr      => msg.Snr,
        _ => null
    };

    /// <summary>Callsigns only contain letters, digits, '-' and '/'. Anything else is rejected (the value ends up in a Flux query).</summary>
    public static bool IsValidCallsign(string s) =>
        s.Length is > 0 and <= 32 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '/');
}
