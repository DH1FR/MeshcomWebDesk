using System.Text.Json;

namespace MeshcomWebDesk.Services.Weather;

/// <summary>
/// Fetches current weather data from Weather Underground (IBM/The Weather Company).
/// API endpoint: https://api.weather.com/v2/pws/observations/current
/// Authentication: API Key + Station ID
/// Units: metric (m) – all values in SI units (°C, hPa, km/h, mm)
/// </summary>
public class WUndergroundProvider : IWeatherProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WUndergroundProvider> _logger;

    private const string BaseUrl = "https://api.weather.com/v2/pws/observations/current";

    public string Name => "Weather Underground";

    public WUndergroundProvider(IHttpClientFactory httpClientFactory, ILogger<WUndergroundProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<WeatherData?> FetchAsync(string apiKey, string stationId, CancellationToken ct = default, bool logRequests = false)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(stationId))
            throw new InvalidOperationException(
                "Weather Underground: Station-ID und API Key müssen eingetragen sein.");

        try
        {
            var client = _httpClientFactory.CreateClient("WeatherApi");
            var url = $"{BaseUrl}?stationId={Uri.EscapeDataString(stationId)}" +
                      $"&format=json&units=m&apiKey={Uri.EscapeDataString(apiKey)}" +
                      $"&numericPrecision=decimal";
            var maskedUrl = url.Replace(apiKey, "***");

            _logger.LogDebug("WUnderground fetch: {Url}", maskedUrl);
            if (logRequests)
                _logger.LogInformation("WUnderground >> GET {Url}", maskedUrl);

            using var response = await client.GetAsync(url, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Weather Underground HTTP {(int)response.StatusCode}: {json.Trim()}");

            // WU returns HTTP 204 with an empty body when the station has not
            // reported within the last ~60 minutes.
            if ((int)response.StatusCode == 204 || string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException(
                    $"Weather Underground: Station {stationId} hat in den letzten 60 Minuten keine Daten gemeldet (HTTP {(int)response.StatusCode}, leere Antwort).");

            if (logRequests)
                _logger.LogInformation("WUnderground << {Status} {Response}", (int)response.StatusCode, json);

            var result = ParseResponse(json);
            if (result == null)
                throw new InvalidOperationException(
                    $"Weather Underground: Antwort empfangen, aber keine bekannten Felder. Antwort: {json.Trim()}");

            return result;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WUnderground fetch failed");
            throw new InvalidOperationException($"Weather Underground Netzwerkfehler: {ex.Message}", ex);
        }
    }

    private WeatherData? ParseResponse(string json)
    {
        try
        {
            // WUnderground response:
            // { "observations": [ { "stationID":"...", "obsTimeUtc":"...",
            //     "metric": { "temp":..., "heatIndex":..., "dewpt":...,
            //                 "windSpeed":..., "windGust":..., "pressure":...,
            //                 "precipRate":..., "precipTotal":..., "elev":... },
            //     "humidity":..., "winddir":..., "uv":..., "solarRadiation":... } ] }

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("observations", out var obsArr) ||
                obsArr.ValueKind != JsonValueKind.Array ||
                obsArr.GetArrayLength() == 0)
            {
                _logger.LogWarning("WUnderground: response contains no observations (station not reporting?): {Json}", json);
                return null;
            }

            var obs = obsArr[0];

            var data = new WeatherData { ProviderName = Name };

            // Top-level fields
            TryAddDouble(obs, "humidity",        data.Fields, WeatherFields.HumidityOutdoor);
            TryAddDouble(obs, "winddir",          data.Fields, WeatherFields.WindDirection);
            TryAddDouble(obs, "uv",               data.Fields, WeatherFields.UvIndex);
            TryAddDouble(obs, "solarRadiation",   data.Fields, WeatherFields.SolarRadiation);

            // Metric sub-object
            if (obs.TryGetProperty("metric", out var m))
            {
                TryAddDouble(m, "temp",         data.Fields, WeatherFields.TempOutdoor);
                TryAddDouble(m, "dewpt",        data.Fields, WeatherFields.DewPoint);
                TryAddDouble(m, "windSpeed",    data.Fields, WeatherFields.WindSpeed);
                TryAddDouble(m, "windGust",     data.Fields, WeatherFields.WindGust);
                TryAddDouble(m, "pressure",     data.Fields, WeatherFields.PressureRelative);
                TryAddDouble(m, "precipRate",   data.Fields, WeatherFields.RainRate);
                TryAddDouble(m, "precipTotal",  data.Fields, WeatherFields.RainDaily);
            }

            // Observation timestamp
            if (obs.TryGetProperty("obsTimeUtc", out var ts) &&
                DateTime.TryParse(ts.GetString(), out var dt))
                data.ObservedUtc = dt.ToUniversalTime();

            return data.Fields.Count > 0 ? data : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WUnderground: failed to parse response: {Json}", json);
            return null;
        }
    }

    private static void TryAddDouble(JsonElement el, string jsonProp, Dictionary<string, double> target, string key)
    {
        if (!el.TryGetProperty(jsonProp, out var prop)) return;
        double val;
        if (prop.ValueKind == JsonValueKind.Number)
            val = prop.GetDouble();
        else if (prop.ValueKind == JsonValueKind.String &&
                 double.TryParse(prop.GetString(),
                     System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            val = parsed;
        else
            return;
        target[key] = val;
    }
}
