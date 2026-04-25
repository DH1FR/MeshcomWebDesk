using System.Text;
using System.Text.Json;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Connects to a configurable MQTT broker and:
/// - Publishes incoming MeshCom events (chat, position, telemetry) to typed topics.
/// - Optionally subscribes to send-topics and forwards them as outgoing UDP messages.
///
/// Topic layout (prefix configurable, default "meshcom"):
///   Published : {prefix}/broadcast          – broadcast chat messages
///               {prefix}/group/{group}       – group chat messages  (e.g. meshcom/group/oe)
///               {prefix}/dm/{callsign}       – direct messages      (e.g. meshcom/dm/DH1FR-1)
///               {prefix}/position/{callsign} – position beacons
///               {prefix}/telemetry/{callsign}– telemetry packets
///
///   Subscribed: {prefix}/send/broadcast      – send a broadcast message
///               {prefix}/send/group/{group}  – send a group message
///               {prefix}/send/dm/{callsign}  – send a direct message
///   Payload for send-topics: JSON { "text": "message text" }
/// </summary>
public sealed class MqttService : IHostedService, IAsyncDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<MqttService>             _logger;
    private readonly IMeshcomSender                   _sender;
    private readonly IMeshcomVariableExpander         _expander;

    private IMqttClient?      _client;
    private CancellationTokenSource? _cts;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public MqttService(
        IOptionsMonitor<MeshcomSettings> settings,
        ILogger<MqttService> logger,
        IMeshcomSender sender,
        IMeshcomVariableExpander expander)
    {
        _settings = settings;
        _logger   = logger;
        _sender   = sender;
        _expander = expander;
    }

    // -------------------------------------------------------------------------
    // IHostedService
    // -------------------------------------------------------------------------

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.CurrentValue.Mqtt.Enabled)
        {
            _logger.LogDebug("MQTT disabled – service not started.");
            return;
        }

        _cts    = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _client = new MqttFactory().CreateMqttClient();

        _client.ConnectedAsync    += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        await ConnectAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true })
        {
            await _client.DisconnectAsync(
                new MqttClientDisconnectOptionsBuilder().WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection).Build(),
                cancellationToken);
        }
        _cts?.Cancel();
    }

    // -------------------------------------------------------------------------
    // Publishing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Publish a MeshCom event to the MQTT broker.
    /// eventType: "message" | "position" | "telemetry"
    /// Fire-and-forget: call with <c>_ = mqtt.PublishAsync(msg, "message")</c>.
    /// </summary>
    public async Task PublishAsync(MeshcomMessage msg, string eventType)
    {
        var cfg = _settings.CurrentValue.Mqtt;
        if (!cfg.Enabled || _client is not { IsConnected: true }) return;

        if (eventType == "message"   && !cfg.PublishMessage)   return;
        if (eventType == "position"  && !cfg.PublishPosition)  return;
        if (eventType == "telemetry" && !cfg.PublishTelemetry) return;

        var topic = BuildPublishTopic(cfg.TopicPrefix, eventType, msg);
        if (topic is null) return;

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
            src_type   = msg.SrcType,
            temp1      = msg.Temp1,
            temp2      = msg.Temp2,
            humidity   = msg.Humidity,
            pressure   = msg.Pressure
        };

        try
        {
            var json = JsonSerializer.Serialize(payload, _jsonOpts);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(json)
                .WithRetainFlag(false)
                .Build();

            await _client.PublishAsync(message);
            if (cfg.LogRequests)
                _logger.LogInformation("MQTT [{Event}] → {Topic}", eventType, topic);
            else
                _logger.LogDebug("MQTT [{Event}] → {Topic}", eventType, topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT publish failed for topic {Topic}", topic);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task ConnectAsync(CancellationToken ct)
    {
        var cfg = _settings.CurrentValue.Mqtt;

        var optBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(cfg.Host, cfg.Port)
            .WithClientId(cfg.ClientId)
            .WithCleanSession();

        if (!string.IsNullOrEmpty(cfg.Username))
            optBuilder = optBuilder.WithCredentials(cfg.Username, cfg.Password);

        if (cfg.UseTls)
            optBuilder = optBuilder.WithTlsOptions(o => o.UseTls());

        try
        {
            _logger.LogInformation("MQTT connecting to {Host}:{Port} …", cfg.Host, cfg.Port);
            await _client!.ConnectAsync(optBuilder.Build(), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT initial connect failed – will retry on reconnect.");
        }
    }

    private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        _logger.LogInformation("MQTT connected.");

        var cfg = _settings.CurrentValue.Mqtt;
        if (!cfg.SubscribeEnabled)
        {
            _logger.LogInformation("MQTT Subscriber deaktiviert – keine Send-Topics abonniert.");
            return;
        }

        var prefix = cfg.TopicPrefix;
        var filters = new[]
        {
            new MqttTopicFilterBuilder().WithTopic($"{prefix}/send/broadcast").Build(),
            new MqttTopicFilterBuilder().WithTopic($"{prefix}/send/group/#").Build(),
            new MqttTopicFilterBuilder().WithTopic($"{prefix}/send/dm/#").Build()
        };

        await _client!.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(filters[0])
            .WithTopicFilter(filters[1])
            .WithTopicFilter(filters[2])
            .Build());

        _logger.LogInformation("MQTT subscribed to send-topics under {Prefix}/send/#", prefix);
    }

    private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (_cts is { IsCancellationRequested: true }) return;

        _logger.LogWarning("MQTT disconnected – reconnecting in 10 s …");
        await Task.Delay(TimeSpan.FromSeconds(10));

        try { await ConnectAsync(_cts!.Token); }
        catch (Exception ex) { _logger.LogError(ex, "MQTT reconnect failed."); }
    }

    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var cfg   = _settings.CurrentValue.Mqtt;
        var topic = args.ApplicationMessage.Topic;
        var body  = args.ApplicationMessage.ConvertPayloadToString();

        _logger.LogDebug("MQTT RX topic={Topic} body={Body}", topic, body);

        string? text = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("text", out var t))
                text = t.GetString();
        }
        catch
        {
            // plain-text fallback
            text = body;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("MQTT RX: empty or missing 'text' field in payload on topic {Topic}", topic);
            return;
        }

        var prefix = cfg.TopicPrefix;

        string destination;
        if (topic == $"{prefix}/send/broadcast")
        {
            destination = "*";
        }
        else if (topic.StartsWith($"{prefix}/send/group/", StringComparison.OrdinalIgnoreCase))
        {
            destination = "#" + topic[($"{prefix}/send/group/").Length..];
        }
        else if (topic.StartsWith($"{prefix}/send/dm/", StringComparison.OrdinalIgnoreCase))
        {
            destination = topic[($"{prefix}/send/dm/").Length..];
        }
        else
        {
            return;
        }

        // Expand variables; for DM pass the destination callsign so {callsign}, {dest-name} etc. resolve.
        var callsign = destination.StartsWith('#') || destination == "*" ? null : destination;
        text = _expander.ExpandVariables(text, callsign);

        if (cfg.LogRequests)
            _logger.LogInformation("MQTT → MeshCom send to {Dest}: {Text}", destination, text);
        else
            _logger.LogDebug("MQTT → MeshCom send to {Dest}: {Text}", destination, text);
        await _sender.SendMessageAsync(destination, text);
    }

    private static string? BuildPublishTopic(string prefix, string eventType, MeshcomMessage msg)
    {
        return eventType switch
        {
            "message" when msg.IsBroadcast                             => $"{prefix}/broadcast",
            "message" when msg.To?.StartsWith('#') == true             => $"{prefix}/group/{msg.To.TrimStart('#').ToLowerInvariant()}",
            "message"                                                  => $"{prefix}/dm/{msg.From}",
            "position"                                                 => $"{prefix}/position/{msg.From}",
            "telemetry"                                                 => $"{prefix}/telemetry/{msg.From}",
            _                                                          => null
        };
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_client is not null)
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
            _client.Dispose();
        }
        _cts?.Dispose();
    }
}
