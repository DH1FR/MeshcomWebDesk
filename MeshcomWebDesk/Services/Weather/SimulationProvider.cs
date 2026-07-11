namespace MeshcomWebDesk.Services.Weather;

/// <summary>
/// Simulation provider for testing – returns realistic, slightly randomized weather data.
/// No API key or network access required.
/// Values are based on typical central European weather and vary slightly on each call
/// to simulate a live feed.
/// </summary>
public class SimulationProvider : IWeatherProvider
{
    private readonly ILogger<SimulationProvider> _logger;
    private static readonly Random _rng = new();

    public string Name => "Simulation";

    public SimulationProvider(ILogger<SimulationProvider> logger)
    {
        _logger = logger;
    }

    public Task<WeatherData?> FetchAsync(string apiKey, string stationId, CancellationToken ct = default, bool logRequests = false)
    {
        _logger.LogInformation("SimulationProvider: generating test data (stationId={StationId})", stationId);

        // Base values typical for central Germany (35279 Neustadt/Hessen area)
        var now   = DateTime.UtcNow;
        var hour  = now.Hour;

        // Temperature: diurnal cycle 8°C (night) … 18°C (afternoon)
        double tempBase = 8.0 + 10.0 * Math.Sin(Math.PI * (hour - 6) / 14.0);
        tempBase = Math.Max(5.0, tempBase);

        var data = new WeatherData
        {
            ProviderName = Name,
            ObservedUtc  = now,
            Fields =
            {
                [WeatherFields.TempOutdoor]      = Round(tempBase + Jitter(0.5)),
                [WeatherFields.TempIndoor]        = Round(tempBase + 4.0 + Jitter(0.3)),
                [WeatherFields.HumidityOutdoor]   = Round(Clamp(65.0 + Jitter(8.0), 30, 99), 0),
                [WeatherFields.HumidityIndoor]    = Round(Clamp(52.0 + Jitter(5.0), 30, 80), 0),
                [WeatherFields.PressureRelative]  = Round(1013.5 + Jitter(3.0), 1),
                [WeatherFields.PressureAbsolute]  = Round(987.2  + Jitter(3.0), 1),
                [WeatherFields.WindSpeed]         = Round(Clamp(12.0 + Jitter(5.0), 0, 60), 1),
                [WeatherFields.WindGust]          = Round(Clamp(22.0 + Jitter(7.0), 0, 80), 1),
                [WeatherFields.WindDirection]     = Round(Clamp(245.0 + Jitter(20.0), 0, 360), 0),
                [WeatherFields.RainRate]          = Round(Clamp(Jitter(0.3),  0, 10), 1),
                [WeatherFields.RainDaily]         = Round(Clamp(1.2  + Jitter(0.5), 0, 50), 1),
                [WeatherFields.UvIndex]           = Round(Clamp(hour is >= 8 and <= 18 ? 2.0 + Jitter(1.5) : 0, 0, 11), 1),
                [WeatherFields.SolarRadiation]    = Round(Clamp(hour is >= 8 and <= 18 ? 300 + Jitter(80)  : 0, 0, 1200), 0),
                [WeatherFields.DewPoint]          = Round(tempBase - 6.0 + Jitter(1.0), 1),
            }
        };

        _logger.LogDebug("SimulationProvider: T={Temp}°C H={Hum}% P={Pres}hPa W={Wind}km/h",
            data.Fields[WeatherFields.TempOutdoor],
            data.Fields[WeatherFields.HumidityOutdoor],
            data.Fields[WeatherFields.PressureRelative],
            data.Fields[WeatherFields.WindSpeed]);

        return Task.FromResult<WeatherData?>(data);
    }

    // Small random variation around zero
    private static double Jitter(double range) => (_rng.NextDouble() * 2 - 1) * range;

    private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

    private static double Round(double v, int decimals = 1) => Math.Round(v, decimals);
}
