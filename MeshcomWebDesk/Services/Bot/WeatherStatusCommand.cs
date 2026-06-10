using System.Text.Json;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.Weather;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// Bot command --weather: returns the current weather provider status and license state.
/// Example reply: "Weather: AWEKAS | Temp=18.4°C Hum=72% | lizenziert OE1KBC-1"
/// </summary>
public class WeatherStatusCommand : IBotCommand
{
    private readonly WeatherApiPollingService _pollingService;
    private readonly IOptions<MeshcomSettings> _settings;

    public string Name        => "weather";
    public string Description => "Wetter-API Status (Provider, Messwerte, Lizenz)";

    public WeatherStatusCommand(WeatherApiPollingService pollingService, IOptions<MeshcomSettings> settings)
    {
        _pollingService = pollingService;
        _settings       = settings;
    }

    public Task<string> ExecuteAsync(string[] args, string senderCallsign)
    {
        var s  = _settings.Value;
        var ws = s.WeatherApi;

        if (ws.Provider == WeatherProvider.None)
        {
            // Fallback: read weather values from the telemetry file when telemetry is active
            // and the mapping contains entries with a WeatherRole (or auto-detectable unit).
            if (s.TelemetryEnabled
                && !string.IsNullOrWhiteSpace(s.TelemetryFilePath)
                && s.TelemetryMapping.Count > 0)
            {
                var telemetryReply = ReadWeatherFromTelemetryFile(s);
                if (telemetryReply is not null)
                    return Task.FromResult(telemetryReply);
            }

            return Task.FromResult("Wetter-API deaktiviert.");
        }

        var sb = new System.Text.StringBuilder();

        // Provider name
        var providerName = ws.Provider switch
        {
            WeatherProvider.Awekas       => "AWEKAS",
            WeatherProvider.WUnderground => "WUnderground",
            WeatherProvider.Simulation   => "Simulation",
            _                            => "Unbekannt"
        };
        sb.Append($"Weather: {providerName}");

        // Last data
        if (_pollingService.LastData?.Fields is { Count: > 0 } fields)
        {
            sb.Append(" |");
            if (fields.TryGetValue(WeatherFields.TempOutdoor, out var temp))
                sb.Append($" T={temp:F1}°C");
            if (fields.TryGetValue(WeatherFields.HumidityOutdoor, out var hum))
                sb.Append($" H={hum:F0}%");
            if (fields.TryGetValue(WeatherFields.PressureRelative, out var pres))
                sb.Append($" P={pres:F0}hPa");
            if (fields.TryGetValue(WeatherFields.WindSpeed, out var wind))
                sb.Append($" W={wind:F1}km/h");

            if (_pollingService.LastFetchUtc.HasValue)
                sb.Append($" ({_pollingService.LastFetchUtc.Value:HH:mm}z)");
        }
        else if (_pollingService.LastError is not null)
        {
            sb.Append($" | Fehler: {_pollingService.LastError}");
        }
        else
        {
            sb.Append(" | Keine Daten");
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Reads weather values (temp, humidity, pressure) from the telemetry JSON file using
    /// the WeatherRole field (or unit-based auto-detection) of each TelemetryMapping entry.
    /// Returns null when the file is missing, unreadable, or contains no weather-role fields.
    /// </summary>
    private static string? ReadWeatherFromTelemetryFile(MeshcomSettings s)
    {
        try
        {
            if (!File.Exists(s.TelemetryFilePath))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(s.TelemetryFilePath));
            var root = doc.RootElement;

            var sb = new System.Text.StringBuilder("Weather (Telemetrie) |");
            bool any = false;

            foreach (var entry in s.TelemetryMapping)
            {
                var role = entry.WeatherRole.Trim().ToLowerInvariant();
                if (role == string.Empty)
                {
                    var unitNorm = entry.Unit.Trim().TrimStart('°').ToLowerInvariant();
                    if      (unitNorm is "c")            role = "temp";
                    else if (unitNorm is "%")            role = "humidity";
                    else if (unitNorm is "hpa" or "mbar") role = "pressure";
                }

                if (role == string.Empty) continue;
                if (!root.TryGetProperty(entry.JsonKey, out var prop)) continue;

                double value;
                if (prop.ValueKind == JsonValueKind.Number)
                    value = prop.GetDouble();
                else if (prop.ValueKind == JsonValueKind.String
                      && double.TryParse(prop.GetString(),
                             System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    value = parsed;
                else continue;

                switch (role)
                {
                    case "temp":     sb.Append($" T={value:F1}°C"); break;
                    case "humidity": sb.Append($" H={value:F0}%");  break;
                    case "pressure": sb.Append($" P={value:F0}hPa"); break;
                }
                any = true;
            }

            if (!any) return null;

            var modified = File.GetLastWriteTimeUtc(s.TelemetryFilePath);
            sb.Append($" ({modified:HH:mm}z)");
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }
}
