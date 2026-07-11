using System.Text.Json;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.Weather;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Periodically fetches weather data from the configured provider (AWEKAS or Weather Underground),
/// applies field mapping and writes the result as JSON to <see cref="MeshcomSettings.TelemetryFilePath"/>.
/// The existing telemetry pipeline then picks up the file and sends it via MeshCom.
/// </summary>
public sealed class WeatherApiPollingService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);

    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly AppLicenseService                _licenseService;
    private readonly AwekasProvider                   _awekasProvider;
    private readonly WUndergroundProvider             _wuProvider;
    private readonly SimulationProvider               _simProvider;
    private readonly ILogger<WeatherApiPollingService> _logger;

    private Timer? _timer;

    /// <summary>Last successfully fetched data (for status display).</summary>
    public WeatherData? LastData { get; private set; }

    /// <summary>UTC timestamp of the last successful fetch.</summary>
    public DateTime? LastFetchUtc { get; private set; }

    /// <summary>Last error message, or null if the last fetch was successful.</summary>
    public string? LastError { get; private set; }

    public WeatherApiPollingService(
        IOptionsMonitor<MeshcomSettings> settings,
        AppLicenseService licenseService,
        AwekasProvider awekasProvider,
        WUndergroundProvider wuProvider,
        SimulationProvider simProvider,
        ILogger<WeatherApiPollingService> logger)
    {
        _settings       = settings;
        _licenseService = licenseService;
        _awekasProvider = awekasProvider;
        _wuProvider     = wuProvider;
        _simProvider    = simProvider;
        _logger         = logger;
    }

    // ── IHostedService ───────────────────────────────────────────────────

    public Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("WeatherApiPollingService starting");
        _timer = new Timer(OnTimer, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null)
            await _timer.DisposeAsync();
    }

    // ── Timer callback ───────────────────────────────────────────────────

    private async void OnTimer(object? state)
    {
        try
        {
            var s = _settings.CurrentValue.WeatherApi;

            if (s.Provider == WeatherProvider.None)
            {
                ScheduleNext(TimeSpan.FromMinutes(5));
                return;
            }

            await PollAsync(s, CancellationToken.None);

            // Lizenzprüfung deaktiviert – konfiguriertes Intervall wird immer verwendet
                var interval = TimeSpan.FromMinutes(Math.Max(MinInterval.TotalMinutes, s.PollIntervalMinutes));

            ScheduleNext(interval);
        }
        catch (Exception ex)
        {
            LastError = $"Interner Fehler: {ex.Message}";
            _logger.LogError(ex, "WeatherApiPollingService: unbehandelte Exception in OnTimer");
            ScheduleNext(TimeSpan.FromMinutes(5));
        }
    }

    private void ScheduleNext(TimeSpan delay)
        => _timer?.Change(delay, Timeout.InfiniteTimeSpan);

    // ── Core polling logic ───────────────────────────────────────────────

    internal async Task PollAsync(WeatherApiSettings s, CancellationToken ct)
    {
        // Wetter-API ist für alle freigegeben (kein gesondertes Feature-Flag).
        // Für zukünftige Einschränkung: _licenseService.HasFeature("WeatherApi")

        IWeatherProvider provider = s.Provider switch
        {
            WeatherProvider.Awekas       => _awekasProvider,
            WeatherProvider.WUnderground => _wuProvider,
            WeatherProvider.Simulation   => _simProvider,
            _                            => throw new InvalidOperationException($"Unknown provider: {s.Provider}")
        };

        _logger.LogDebug("WeatherApi polling {Provider}", provider.Name);

        WeatherData? data;
        try
        {
            data = await provider.FetchAsync(s.ApiKey, s.StationId, ct, s.LogRequests);
        }
        catch (InvalidOperationException ex)
        {
            LastError = ex.Message;
            _logger.LogWarning("WeatherApi: {Error}", ex.Message);
            return;
        }

        if (data == null)
        {
            LastError = $"{provider.Name}: Keine Daten empfangen";
            _logger.LogWarning("WeatherApi: no data from {Provider}", provider.Name);
            return;
        }

        LastError = null;
        LastData  = data;
        LastFetchUtc = DateTime.UtcNow;

        await WriteJsonFileAsync(s, data, ct);
    }

    /// <summary>
    /// Writes weather data as a flat JSON object to TelemetryFilePath.
    /// Keys are the WeatherFields constants (e.g. "temp_out", "humidity_out") – use these
    /// directly as JSON-Key values in the Telemetry Mapping configuration.
    /// </summary>
    private async Task WriteJsonFileAsync(WeatherApiSettings s, WeatherData data, CancellationToken ct)
    {
        var mainSettings = _settings.CurrentValue;

        if (string.IsNullOrWhiteSpace(mainSettings.TelemetryFilePath))
        {
            _logger.LogWarning("WeatherApi: TelemetryFilePath is not configured – data not written");
            return;
        }

        // Write fields directly using WeatherFields constants as JSON keys.
        // Configure the Telemetry Mapping with e.g. JSON-Key "temp_out", Label "T.out", Unit "°C".
        var output = new Dictionary<string, object>();

        foreach (var (fieldKey, value) in data.Fields)
            output[fieldKey] = value;

        // Metadata fields (prefixed with _ so they don't clash with weather fields)
        output["_provider"]  = data.ProviderName;
        output["_observed"]  = data.ObservedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(mainSettings.TelemetryFilePath);
        try
        {
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(mainSettings.TelemetryFilePath, json, System.Text.Encoding.UTF8, ct);
        }
        catch (UnauthorizedAccessException ex)
        {
            LastError = $"TelemetryFilePath nicht beschreibbar: {mainSettings.TelemetryFilePath}";
            _logger.LogError(
                "WeatherApi: cannot write to TelemetryFilePath '{Path}' – access denied. " +
                "Use a relative path (e.g. 'data/telemetry.json') or a path the service account can write to. " +
                "Error: {Message}", mainSettings.TelemetryFilePath, ex.Message);
            return;
        }

        _logger.LogInformation("WeatherApi: wrote {Count} fields to {Path}",
            data.Fields.Count, mainSettings.TelemetryFilePath);
    }
}
