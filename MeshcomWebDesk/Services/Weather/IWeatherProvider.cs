namespace MeshcomWebDesk.Services.Weather;

/// <summary>
/// Standard weather field names used as keys in the telemetry JSON output.
/// These names can be mapped to TelemetryMapping JSON-Keys via WeatherApiSettings.FieldMapping.
/// </summary>
public static class WeatherFields
{
    public const string TempOutdoor      = "temp_out";
    public const string TempIndoor       = "temp_in";
    public const string HumidityOutdoor  = "humidity_out";
    public const string HumidityIndoor   = "humidity_in";
    public const string PressureRelative = "pressure_rel";
    public const string PressureAbsolute = "pressure_abs";
    public const string WindSpeed        = "wind_speed";
    public const string WindGust         = "wind_gust";
    public const string WindDirection    = "wind_dir";
    public const string RainRate         = "rain_rate";
    public const string RainDaily        = "rain_day";
    public const string UvIndex          = "uv";
    public const string SolarRadiation   = "solar_rad";
    public const string DewPoint         = "dew_point";
}

/// <summary>
/// Result of a weather data fetch from an external provider.
/// Field keys are standard WeatherFields constants (or provider-specific if not mapped).
/// </summary>
public class WeatherData
{
    /// <summary>Dictionary of field name → value.</summary>
    public Dictionary<string, double> Fields { get; set; } = new();

    /// <summary>UTC timestamp of the observation.</summary>
    public DateTime ObservedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Name of the provider that supplied this data.</summary>
    public string ProviderName { get; set; } = string.Empty;
}

/// <summary>
/// Abstraction for an external weather data source.
/// </summary>
public interface IWeatherProvider
{
    /// <summary>Display name of the provider (e.g. "AWEKAS", "Weather Underground").</summary>
    string Name { get; }

    /// <summary>
    /// Fetches the latest observation from the provider.
    /// Returns null if the data could not be retrieved.
    /// When <paramref name="logRequests"/> is true, the request (masked API key) and raw
    /// response are written to the log at Information level.
    /// </summary>
    Task<WeatherData?> FetchAsync(string apiKey, string stationId, CancellationToken ct = default, bool logRequests = false);
}
