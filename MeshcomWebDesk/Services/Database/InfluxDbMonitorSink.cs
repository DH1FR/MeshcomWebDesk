using System.Net.Http.Headers;
using System.Text;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.Database;

public sealed class InfluxDbMonitorSink
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<InfluxDbMonitorSink>     _logger;
    private static readonly HttpClient _http = new();

    public InfluxDbMonitorSink(IOptionsMonitor<MeshcomSettings> settings, ILogger<InfluxDbMonitorSink> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    public async Task WriteAsync(MeshcomMessage msg, CancellationToken ct = default)
    {
        var db = _settings.CurrentValue.Database;
        try
        {
            var line = BuildLineProtocol(msg);
            var url  = $"{db.InfluxUrl.TrimEnd('/')}/api/v2/write" +
                       $"?org={Uri.EscapeDataString(db.InfluxOrg)}" +
                       $"&bucket={Uri.EscapeDataString(db.InfluxBucket)}" +
                       $"&precision=ms";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", db.InfluxToken);
            req.Content = new StringContent(line, Encoding.UTF8, "text/plain");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogError("InfluxDB: HTTP {Status} – {Body}", (int)resp.StatusCode, body);
            }
            else if (db.LogInserts)
            {
                _logger.LogInformation(
                    "DB WRITE [influx:{Bucket}] {From} → {To} | {Text}",
                    db.InfluxBucket, msg.From, msg.To,
                    msg.Text.Length > 60 ? msg.Text[..60] + "…" : msg.Text);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InfluxDB: Fehler beim Schreiben der Monitor-Daten");
        }
    }

    private static string BuildLineProtocol(MeshcomMessage msg)
    {
        var tags = new StringBuilder("meshcom_monitor");
        tags.Append($",from={EscapeTag(msg.From)}");
        tags.Append($",to={EscapeTag(msg.To)}");
        if (!string.IsNullOrEmpty(msg.SrcType))
            tags.Append($",src_type={EscapeTag(msg.SrcType)}");
        if (msg.HwId.HasValue)
            tags.Append($",hw_id={msg.HwId.Value}i");

        var fields = new List<string>();
        if (!string.IsNullOrEmpty(msg.Text))      fields.Add($"text={EscapeStr(msg.Text)}");
        if (msg.Rssi.HasValue)                     fields.Add($"rssi={msg.Rssi.Value}i");
        if (msg.Snr.HasValue)                      fields.Add($"snr={msg.Snr.Value:F3}");
        if (msg.Latitude.HasValue)                 fields.Add($"latitude={msg.Latitude.Value:F6}");
        if (msg.Longitude.HasValue)                fields.Add($"longitude={msg.Longitude.Value:F6}");
        if (msg.Altitude.HasValue)                 fields.Add($"altitude={msg.Altitude.Value}i");
        if (msg.Battery.HasValue)                  fields.Add($"battery={msg.Battery.Value}i");
        if (!string.IsNullOrEmpty(msg.Firmware))   fields.Add($"firmware={EscapeStr(msg.Firmware)}");
        if (!string.IsNullOrEmpty(msg.RelayPath))  fields.Add($"relay_path={EscapeStr(msg.RelayPath)}");
        if (msg.Temp1.HasValue)                    fields.Add($"temp1={msg.Temp1.Value:F2}");
        if (msg.Temp2.HasValue)                    fields.Add($"temp2={msg.Temp2.Value:F2}");
        if (msg.Humidity.HasValue)                 fields.Add($"humidity={msg.Humidity.Value:F1}");
        if (msg.Pressure.HasValue)                 fields.Add($"pressure={msg.Pressure.Value:F2}");
        fields.Add($"is_outgoing={msg.IsOutgoing.ToString().ToLowerInvariant()}");
        fields.Add($"is_position_beacon={msg.IsPositionBeacon.ToString().ToLowerInvariant()}");
        fields.Add($"is_telemetry={msg.IsTelemetry.ToString().ToLowerInvariant()}");

        var ts = ((DateTimeOffset)msg.Timestamp).ToUnixTimeMilliseconds();
        return $"{tags} {string.Join(",", fields)} {ts}";
    }

    private static string EscapeTag(string v) =>
        v.Replace(",", @"\,").Replace(" ", @"\ ").Replace("=", @"\=");

    private static string EscapeStr(string v) =>
        $"\"{v.Replace("\\", @"\\").Replace("\"", "\\\"").Replace("\n", @"\n")}\"";
}
