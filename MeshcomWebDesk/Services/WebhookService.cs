using System.Text;
using System.Text.Json;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Sends a JSON payload via HTTP POST to a configurable URL when specific
/// MeshCom events occur (incoming message, position beacon, telemetry).
/// Errors are logged and swallowed – never throws.
/// </summary>
public sealed class WebhookService
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<WebhookService>          _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public WebhookService(IOptionsMonitor<MeshcomSettings> settings, ILogger<WebhookService> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>
    /// Fire-and-forget: call with <c>_ = webhook.SendAsync(msg, "message")</c>.
    /// eventType: "message" | "position" | "telemetry"
    /// </summary>
    public async Task SendAsync(MeshcomMessage msg, string eventType)
    {
        var wh = _settings.CurrentValue.Webhook;

        if (!wh.Enabled || string.IsNullOrWhiteSpace(wh.Url)) return;

        if (eventType == "message"   && !wh.OnMessage)   return;
        if (eventType == "position"  && !wh.OnPosition)  return;
        if (eventType == "telemetry" && !wh.OnTelemetry) return;

        var payload = new
        {
            @event     = eventType,
            timestamp  = msg.Timestamp,
            from       = msg.From,
            to         = msg.To,
            text       = string.IsNullOrEmpty(msg.Text) ? null : msg.Text,
            rssi       = msg.Rssi,
            snr        = msg.Snr,
            latitude   = msg.Latitude,
            longitude  = msg.Longitude,
            altitude   = msg.Altitude,
            battery    = msg.Battery,
            firmware   = msg.Firmware,
            relay_path = msg.RelayPath,
            src_type   = msg.SrcType
        };

        try
        {
            var json = JsonSerializer.Serialize(payload, _jsonOpts);
            using var req = new HttpRequestMessage(HttpMethod.Post, wh.Url);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);

            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("Webhook: HTTP {Status} von {Url}", (int)resp.StatusCode, wh.Url);
            else
                _logger.LogDebug("Webhook [{Event}] → {Url}", eventType, wh.Url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook: Fehler beim Senden an {Url}", wh.Url);
        }
    }
}
