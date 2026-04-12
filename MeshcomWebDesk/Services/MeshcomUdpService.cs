using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Helpers;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.Bot;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Background service that handles UDP communication with a MeshCom device.
/// Listens for incoming messages and provides a method to send messages.
/// 
/// MeshCom EXTUDP JSON format:
///   RX chat : {"src_type":"lora","type":"msg","src":"NOCALL-1","dst":"NOCALL-2","msg":"Hello{034","rssi":-95,"snr":12,...}
///   RX pos  : {"src_type":"node","type":"pos","src":"NOCALL-2","lat":50.8515,"lat_dir":"N","long":9.1075,"long_dir":"E","alt":827,...}
///   TX chat : {"type":"msg","dst":"NOCALL-1","msg":"Hello"}
/// </summary>
public partial class MeshcomUdpService : BackgroundService
{
    private readonly ILogger<MeshcomUdpService> _logger;
    private readonly ChatService _chatService;
    private readonly QrzService  _qrzService;
    private readonly BotCommandService _botCommandService;
    private MeshcomSettings _settings;
    private UdpClient? _udpClient;

    /// <summary>Assembly version, resolved once at startup (e.g. "1.4.1").</summary>
    private static readonly string AppVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;

    /// <summary>Live connection and statistics status. Updated on every relevant event.</summary>
    public ConnectionStatus Status { get; } = new();

    /// <summary>Raised whenever <see cref="Status"/> changes so UI components can refresh.</summary>
    public event Action? OnStatusChange;

    /// <summary>Matches trailing MeshCom sequence markers like {034, {333 at end of message text.</summary>
    [GeneratedRegex(@"\{\d+$")]
    private static partial Regex TrailingSequencePattern();

    /// <summary>Matches APRS-style ACK messages, e.g. "NOCALL-2 :ack187" or "NOCALL-2  :ack187" (padded addressee).</summary>
    [GeneratedRegex(@"^\S+\s+:ack\d+$")]
    private static partial Regex AckPattern();

    /// <summary>Captures the sequence number from a trailing {NNN} marker, e.g. "{034" → "034".</summary>
    [GeneratedRegex(@"\{(\d+)$")]
    private static partial Regex SequenceNumberPattern();

    /// <summary>Captures the sequence number from an APRS ACK text, e.g. "NOCALL-2  :ack034" → "034".</summary>
    [GeneratedRegex(@":ack(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AckSequencePattern();

    /// <summary>Detects MeshCom network time-sync broadcasts, e.g. "{CET}2026-04-07 18:11:58".</summary>
    [GeneratedRegex(@"^\{[A-Z]{2,5}\}\d{4}-\d{2}-\d{2}")]
    private static partial Regex TimeSyncPattern();

    public MeshcomUdpService(
        ILogger<MeshcomUdpService> logger,
        IOptionsMonitor<MeshcomSettings> settings,
        ChatService chatService,
        QrzService qrzService,
        BotCommandService botCommandService)
    {
        _logger             = logger;
        _chatService        = chatService;
        _qrzService         = qrzService;
        _botCommandService  = botCommandService;
        _settings           = settings.CurrentValue;
        settings.OnChange(s =>
        {
            _settings = s;
            _logger.LogInformation("Settings reloaded from appsettings.json.");
        });

        _chatService.OnNewDirectTab += callsign => _ = SendAutoReplyAsync(callsign);
        _chatService.OnBotCommand   += msg      => _ = HandleBotCommandAsync(msg);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MeshCom UDP service starting – listening on {Ip}:{Port}, device at {DevIp}:{DevPort}",
            _settings.ListenIp, _settings.ListenPort, _settings.DeviceIp, _settings.DevicePort);

        try
        {
            var localEp = new IPEndPoint(IPAddress.Parse(_settings.ListenIp), _settings.ListenPort);
            _udpClient = new UdpClient(localEp);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to bind UDP socket on {Ip}:{Port}", _settings.ListenIp, _settings.ListenPort);
            return;
        }

        Status.IsListening = true;
        NotifyStatusChange();

        // Send registration packet so the device adds this client to its sender list.
        // Without this, the device does not know where to deliver UDP data.
        await RegisterWithDeviceAsync();

        // Beacon task runs permanently and checks BeaconEnabled on each tick.
        _ = RunBeaconAsync(stoppingToken);

        // Telemetry task runs permanently and checks TelemetryEnabled on each tick.
        _ = RunTelemetryAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync(stoppingToken);
                    var raw = Encoding.UTF8.GetString(result.Buffer).TrimEnd('\r', '\n');

                    if (string.IsNullOrWhiteSpace(raw))
                        continue;

                    _logger.LogDebug("UDP RX [{Remote}]: {Data}", result.RemoteEndPoint, raw);
                    if (_settings.LogUdpTraffic)
                        _logger.LogInformation("[UDP-RX] {Remote} {Data}", result.RemoteEndPoint, raw);
                    var message = ParseMessage(raw);

                    if (message != null)
                    {
                        // Update signal stats from LoRa metadata
                        if (message.Rssi.HasValue)
                        {
                            Status.LastRssi = message.Rssi;
                            Status.LastSnr = message.Snr;
                        }

                        // Skip node echoes of our own sent messages (already recorded as outgoing).
                        // Still extract own GPS position from the echo if present.
                        if (string.Equals(message.From, _settings.MyCallsign, StringComparison.OrdinalIgnoreCase))
                        {
                            if (message.Latitude.HasValue && message.Longitude.HasValue)
                            {
                                SetOwnPosition(message.Latitude.Value, message.Longitude.Value,
                                               message.Altitude, "Node");
                            }
                            // Assign node-assigned sequence number to matching outgoing message
                            if (message.SequenceNumber != null)
                                _chatService.AssignOutgoingSequence(message.To, message.SequenceNumber);
                            _logger.LogDebug("Skipping node echo from {From}", message.From);
                            // Do not add node echoes to the monitor – the TX entry is already shown there.
                        }
                        else if (message.IsTimeSync)
                        {
                            // Time-sync broadcast: monitor only, no chat tab
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddRawMessage(message);
                        }
                        else if (message.IsAck)
                        {
                            // APRS ACK – mark as delivered, update relay path in MH list, add to monitor
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddAck(message);
                        }
                        else if (message.IsPositionBeacon)
                        {
                            // Pure position beacon: update MH list but don't open a chat tab
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddPositionBeacon(message);
                        }
                        else if (message.IsTelemetry)
                        {
                            // Telemetry packet: update MH list, show in monitor only
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddTelemetry(message);
                        }
                        else
                        {
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            _chatService.AddIncomingMessage(message);
                        }
                    }
                    else
                    {
                        // Unparseable data (status, telemetry, etc.) – raw feed only, no tab
                        _chatService.AddRawMessage(new MeshcomMessage
                        {
                            Text = raw,
                            RawData = raw
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving UDP data");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        finally
        {
            _udpClient.Dispose();
            _udpClient = null;
            Status.IsListening = false;
            Status.IsRegistered = false;
            NotifyStatusChange();
            _logger.LogInformation("MeshCom UDP service stopped");
        }
    }

    /// <summary>
    /// Send a registration packet to the MeshCom device so it adds this client
    /// to its UDP sender list and starts delivering data.
    /// </summary>
    private async Task RegisterWithDeviceAsync()
    {
        if (_udpClient == null) return;

        try
        {
            var json = JsonSerializer.Serialize(new { type = "info", src = _settings.MyCallsign });
            var bytes = Encoding.UTF8.GetBytes(json);
            var remoteEp = new IPEndPoint(IPAddress.Parse(_settings.DeviceIp), _settings.DevicePort);

            await _udpClient.SendAsync(bytes, bytes.Length, remoteEp);
            _logger.LogInformation("UDP registration packet sent to {DeviceIp}:{DevicePort}", _settings.DeviceIp, _settings.DevicePort);
            if (_settings.LogUdpTraffic)
                _logger.LogInformation("[UDP-TX] {Remote} {Data}", remoteEp, json);
            Status.IsRegistered = true;
            NotifyStatusChange();
            _chatService.AddRawMessage(new MeshcomMessage
            {
                From      = _settings.MyCallsign,
                IsOutgoing = true,
                Text      = json,
                RawData   = json
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send UDP registration packet to {DeviceIp}:{DevicePort}", _settings.DeviceIp, _settings.DevicePort);
        }
    }

    private Task SendAutoReplyAsync(string callsign)
    {
        if (!_settings.AutoReplyEnabled || string.IsNullOrWhiteSpace(_settings.AutoReplyText))
            return Task.CompletedTask;

        var text = ExpandVariables(_settings.AutoReplyText, callsign);
        _logger.LogInformation("Auto-reply to new contact {Callsign}", callsign);
        return SendMessageAsync(callsign, text);
    }

    private async Task HandleBotCommandAsync(MeshcomMessage message)
    {
        if (!_settings.BotEnabled) return;

        var reply = await _botCommandService.ExecuteAsync(message.Text!, message.From);
        reply = ExpandVariables(reply, message.From);
        _logger.LogInformation("Bot reply to {From}: {Reply}", message.From, reply);
        await SendMessageAsync(message.From, reply);
    }

    /// <summary>
    /// Substitutes all supported template variables in <paramref name="template"/>.
    /// When <paramref name="callsign"/> is provided, caller-specific variables are resolved.
    /// Variables with no value available are replaced with an empty string.
    /// </summary>
    private string ExpandVariables(string template, string? callsign = null)
    {
        var now     = DateTime.Now;
        var station = callsign != null
            ? _chatService.MhList.FirstOrDefault(s =>
                string.Equals(s.Callsign, callsign, StringComparison.OrdinalIgnoreCase))
            : null;

        QrzInfo? qrz = null;
        if (callsign != null && _settings.Qrz.Enabled)
            _qrzService.TryGetCached(callsign, out qrz);

        var route = station?.LastRelayPath != null
            ? string.Join(" \u2192 ", station.LastRelayPath.Split(',').Select(h => h.Trim()))
            : string.Empty;

        return template
            .Replace("{version}",   AppVersion,                                        StringComparison.OrdinalIgnoreCase)
            .Replace("{mycall}",    _settings.MyCallsign,                              StringComparison.OrdinalIgnoreCase)
            .Replace("{callsign}",  callsign              ?? string.Empty,             StringComparison.OrdinalIgnoreCase)
            .Replace("{dest-name}", qrz?.FirstName        ?? string.Empty,             StringComparison.OrdinalIgnoreCase)
            .Replace("{dest-loc}",  qrz?.Location         ?? string.Empty,             StringComparison.OrdinalIgnoreCase)
            .Replace("{rssi}",      station?.LastRssi?.ToString()      ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{snr}",       station?.LastSnr?.ToString("F1")   ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{hw}",        MeshcomLookup.HwName(station?.HwId),               StringComparison.OrdinalIgnoreCase)
            .Replace("{route}",     route,                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{hops}",      station?.HopCount.ToString()       ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}",      now.ToString("dd.MM.yyyy"),                        StringComparison.OrdinalIgnoreCase)
            .Replace("{time}",      now.ToString("HH:mm"),                             StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks beacon settings every minute. Sends a beacon to <see cref="MeshcomSettings.BeaconGroup"/>
    /// when <see cref="MeshcomSettings.BeaconEnabled"/> is true and the configured interval has elapsed.
    /// All settings are read from the live <see cref="_settings"/> field so changes apply without restart.
    /// </summary>
    private async Task RunBeaconAsync(CancellationToken stoppingToken)
    {
        var lastSent = DateTime.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            var s = _settings;
            bool configured = s.BeaconEnabled
                && !string.IsNullOrWhiteSpace(s.BeaconGroup)
                && !string.IsNullOrWhiteSpace(s.BeaconText);

            if (!configured)
            {
                SetBeaconStatus(false, null);
                continue;
            }

            var interval = TimeSpan.FromHours(Math.Max(1, s.BeaconIntervalHours));

            // On first activation, record the current time so the first beacon fires
            // after one full interval – not immediately on startup or when enabling.
            if (lastSent == DateTime.MinValue)
                lastSent = DateTime.Now;

            var nextDue = lastSent + interval;

            SetBeaconStatus(true, nextDue > DateTime.Now ? nextDue : DateTime.Now);

            if (DateTime.Now < nextDue)
                continue;

            var destination = s.BeaconGroup.TrimStart('#');
            var beaconText  = ExpandVariables(s.BeaconText);
            _logger.LogInformation("Sending beacon to {Group}", s.BeaconGroup);
            await SendMessageAsync(destination, beaconText, s.BeaconGroup);
            lastSent = DateTime.Now;
            SetBeaconStatus(true, lastSent + interval);
        }

        SetBeaconStatus(false, null);
    }

    private void SetBeaconStatus(bool active, DateTime? nextSend)
    {
        if (Status.BeaconActive == active && Status.BeaconNextSend == nextSend) return;
        Status.BeaconActive  = active;
        Status.BeaconNextSend = nextSend;
        NotifyStatusChange();
    }

    /// <summary>
    /// Checks telemetry settings every minute. Reads the configured JSON file and sends a
    /// formatted telemetry message to <see cref="MeshcomSettings.TelemetryGroup"/> when
    /// <see cref="MeshcomSettings.TelemetryEnabled"/> is true and the interval has elapsed.
    /// </summary>
    private async Task RunTelemetryAsync(CancellationToken stoppingToken)
    {
        // Track the (date, hour) slot already sent to avoid double-firing in the same hour
        var lastSentSlot = (Date: DateOnly.MinValue, Hour: -1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            var s = _settings;
            bool configured = s.TelemetryEnabled
                && !string.IsNullOrWhiteSpace(s.TelemetryFilePath)
                && !string.IsNullOrWhiteSpace(s.TelemetryGroup)
                && s.TelemetryMapping.Count > 0;

            if (!configured)
            {
                SetTelemetryStatus(false, null);
                continue;
            }

            var scheduleHours = ParseScheduleHours(s.TelemetryScheduleHours);
            if (scheduleHours.Count == 0)
            {
                SetTelemetryStatus(false, null);
                continue;
            }

            var now         = DateTime.Now;
            var currentSlot = (DateOnly.FromDateTime(now), now.Hour);

            SetTelemetryStatus(true, ComputeNextScheduledTime(scheduleHours, now));

            if (scheduleHours.Contains(now.Hour) && lastSentSlot != currentSlot)
            {
                await SendTelemetryAsync(s);
                lastSentSlot = currentSlot;
                SetTelemetryStatus(true, ComputeNextScheduledTime(scheduleHours, DateTime.Now));
            }
        }

        SetTelemetryStatus(false, null);
    }

    private static HashSet<int> ParseScheduleHours(string input) =>
        input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(p => int.TryParse(p, out var h) && h >= 0 && h <= 23 ? h : -1)
             .Where(h => h >= 0)
             .ToHashSet();

    private static DateTime ComputeNextScheduledTime(HashSet<int> hours, DateTime from)
    {
        var sorted   = hours.OrderBy(h => h).ToList();
        var nextHour = sorted.FirstOrDefault(h => h > from.Hour, -1);
        return nextHour >= 0
            ? from.Date.AddHours(nextHour)
            : from.Date.AddDays(1).AddHours(sorted[0]);
    }

    /// <summary>
    /// Immediately reads the telemetry file and sends all configured values.
    /// Used by the Settings UI for manual test sends without waiting for the interval.
    /// </summary>
    public Task SendTelemetryNowAsync() => SendTelemetryAsync(_settings);

    /// <summary>
    /// Immediately sends the configured beacon to <see cref="MeshcomSettings.BeaconGroup"/>.
    /// Used by the Settings UI to test the beacon without waiting for the interval.
    /// </summary>
    public Task SendBeaconNowAsync()
    {
        var s = _settings;
        if (string.IsNullOrWhiteSpace(s.BeaconGroup))
            throw new InvalidOperationException("Keine Baken-Gruppe konfiguriert.");
        if (string.IsNullOrWhiteSpace(s.BeaconText))
            throw new InvalidOperationException("Kein Bakentext konfiguriert.");

        var destination = s.BeaconGroup.TrimStart('#');
        var beaconText  = ExpandVariables(s.BeaconText);
        _logger.LogInformation("Beacon test send to {Group}: {Text}", s.BeaconGroup, beaconText);
        return SendMessageAsync(destination, beaconText, s.BeaconGroup);
    }

    /// <summary>
    /// Immediately sends the configured auto-reply text to <paramref name="callsign"/>.
    /// Used by the Settings UI to test the auto-reply without waiting for an incoming message.
    /// </summary>
    public Task SendAutoReplyNowAsync(string callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign))
            throw new ArgumentException("Kein Rufzeichen angegeben.");
        if (string.IsNullOrWhiteSpace(_settings.AutoReplyText))
            throw new InvalidOperationException("Kein Auto-Reply Text konfiguriert.");

        var text = ExpandVariables(_settings.AutoReplyText, callsign);
        _logger.LogInformation("Auto-reply test send to {Callsign}: {Text}", callsign, text);
        return SendMessageAsync(callsign, text);
    }

    private async Task SendTelemetryAsync(MeshcomSettings s)
    {
        try
        {
            if (!File.Exists(s.TelemetryFilePath))
            {
                _logger.LogWarning("Telemetry file not found: {Path}", s.TelemetryFilePath);
                return;
            }

            var fileContent = await File.ReadAllTextAsync(s.TelemetryFilePath);
            using var doc = JsonDocument.Parse(fileContent);
            var root = doc.RootElement;

            var parts = new List<string>();
            double? ownTemp = null, ownHumidity = null, ownPressure = null;

            foreach (var entry in s.TelemetryMapping)
            {
                if (string.IsNullOrWhiteSpace(entry.JsonKey)) continue;
                if (!root.TryGetProperty(entry.JsonKey, out var valueProp)) continue;

                double value;
                if (valueProp.ValueKind == JsonValueKind.Number)
                    value = valueProp.GetDouble();
                else if (valueProp.ValueKind == JsonValueKind.String &&
                         double.TryParse(valueProp.GetString(),
                             System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    value = parsed;
                else
                    continue;

                // Capture well-known weather values for map popup.
                // Explicit WeatherRole takes precedence; unit-based detection is the fallback
                // for entries that were configured before the Role field was introduced.
                var role = entry.WeatherRole.Trim().ToLowerInvariant();
                if (role == string.Empty)
                {
                    var unitNorm = entry.Unit.Trim().TrimStart('°').ToLowerInvariant();
                    if      (unitNorm is "c")                      role = "temp";
                    else if (unitNorm is "%")                      role = "humidity";
                    else if (unitNorm is "hpa" or "mbar" or "mb")  role = "pressure";
                }
                if      (role == "temp")     ownTemp     ??= value;
                else if (role == "humidity") ownHumidity ??= value;
                else if (role == "pressure") ownPressure ??= value;

                var decimals  = Math.Max(0, entry.Decimals);
                var formatted = value.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
                var label     = string.IsNullOrWhiteSpace(entry.Label) ? entry.JsonKey : entry.Label;
                parts.Add($"{label}={formatted}{entry.Unit}");
            }

            if (parts.Count == 0)
            {
                _logger.LogWarning("No telemetry values could be read from {Path}", s.TelemetryFilePath);
                return;
            }

            // Build prefix from own GPS position (Maidenhead locator) or fall back to "TM"
            var locator = (Status.OwnLatitude.HasValue && Status.OwnLongitude.HasValue)
                ? Helpers.GeoHelper.ToMaidenhead(Status.OwnLatitude.Value, Status.OwnLongitude.Value)
                : null;

            // Pack parts into 150-char buckets.
            // Reserve up to 11 chars for the longest prefix ("JN48QN/99:") + 1 space.
            const int MaxMsg    = 150;
            const int PrefixMax = 11;
            const int BucketLen = MaxMsg - PrefixMax;

            var buckets = new List<string>();
            var current = new System.Text.StringBuilder();

            foreach (var part in parts)
            {
                var sep = current.Length == 0 ? string.Empty : " ";
                if (current.Length + sep.Length + part.Length > BucketLen)
                {
                    if (current.Length > 0)
                        buckets.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(part);
            }
            if (current.Length > 0)
                buckets.Add(current.ToString());

            var destination = s.TelemetryGroup.TrimStart('#');

            for (int i = 0; i < buckets.Count; i++)
            {
                // Single bucket  → "JN48QN:"  or "TM:"
                // Multiple buckets → "JN48QN/1:" or "TM1:"
                string prefix = buckets.Count == 1
                    ? (locator ?? "TM") + ":"
                    : (locator ?? "TM") + $"/{i + 1}:";
                var msg    = $"{prefix} {buckets[i]}";

                _logger.LogInformation(
                    "Sending telemetry [{Index}/{Total}] to {Group}: {Msg}",
                    i + 1, buckets.Count, s.TelemetryGroup, msg);

                await SendMessageAsync(destination, msg, s.TelemetryGroup);

                // Brief pause between consecutive packets to avoid node flooding
                if (i < buckets.Count - 1)
                    await Task.Delay(TimeSpan.FromSeconds(2));
            }

            // Persist captured weather values in Status so the map popup can show them
            if (ownTemp.HasValue || ownHumidity.HasValue || ownPressure.HasValue)
            {
                Status.OwnTemp             = ownTemp;
                Status.OwnHumidity         = ownHumidity;
                Status.OwnPressure         = ownPressure;
                Status.OwnTelemetrySentTime = DateTime.UtcNow;
                NotifyStatusChange();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading or sending telemetry from {Path}", s.TelemetryFilePath);
        }
    }

    private void SetTelemetryStatus(bool active, DateTime? nextSend)
    {
        if (Status.TelemetryActive == active && Status.TelemetryNextSend == nextSend) return;
        Status.TelemetryActive  = active;
        Status.TelemetryNextSend = nextSend;
        NotifyStatusChange();
    }

    /// <summary>
    /// Send a text message to the MeshCom device via UDP.
    /// </summary>
    /// <param name="destination">Wire destination sent to the node (no leading '#', e.g. "9" for group #9).</param>
    /// <param name="text">Message text.</param>
    /// <param name="tabKey">Original chat-tab key (e.g. "#9", "*"). Used for local tab routing only.
    /// When null, <paramref name="destination"/> is used for both wire and tab routing.</param>
    public async Task SendMessageAsync(string destination, string text, string? tabKey = null)
    {
        if (_udpClient == null)
        {
            _logger.LogWarning("Cannot send – UDP client not initialized");
            return;
        }

        if (text.Length > 149)
        {
            _logger.LogWarning(
                "Nachricht zu lang ({Length} Zeichen, max 149) – Senden abgebrochen: \"{Excerpt}\"",
                text.Length, text[..Math.Min(60, text.Length)]);
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(new { type = "msg", dst = destination, msg = text });
            var bytes = Encoding.UTF8.GetBytes(json);
            var remoteEp = new IPEndPoint(IPAddress.Parse(_settings.DeviceIp), _settings.DevicePort);

            await _udpClient.SendAsync(bytes, bytes.Length, remoteEp);
            _logger.LogDebug("UDP TX [{Remote}]: {Data}", remoteEp, json);
            if (_settings.LogUdpTraffic)
                _logger.LogInformation("[UDP-TX] {Remote} {Data}", remoteEp, json);

            Status.TxCount++;
            Status.LastTxTime = DateTime.Now;
            NotifyStatusChange();

            _chatService.AddOutgoingMessage(new MeshcomMessage
            {
                From = _settings.MyCallsign,
                To = tabKey ?? destination,
                Text = text,
                IsOutgoing = true,
                RawData = json
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending UDP data to {DeviceIp}:{DevicePort}", _settings.DeviceIp, _settings.DevicePort);
        }
    }

    private void NotifyStatusChange() => OnStatusChange?.Invoke();

    /// <summary>
    /// Updates the own GPS position (called from browser geolocation or node position beacon).
    /// </summary>
    public void SetOwnPosition(double lat, double lon, int? altMeters, string source)
    {
        Status.OwnLatitude = lat;
        Status.OwnLongitude = lon;
        Status.OwnAltitude = altMeters;
        Status.OwnPositionSource = source;
        NotifyStatusChange();
        _logger.LogInformation("Own position updated [{Source}]: {Lat:F5}, {Lon:F5}, {Alt}m",
            source, lat, lon, altMeters);
    }

    /// <summary>
    /// Strips the MeshCom EXTUDP wrapper so the inner JSON can be parsed.
    ///   "[EXT] Out: {JSON} Len: NNN"  →  "{JSON}"
    /// Returns the input unchanged when no wrapper is present.
    /// </summary>
    private static string UnwrapExtMessage(string raw)
    {
        const string prefix = "[EXT] Out: ";
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
            return raw;

        var jsonStart = prefix.Length;
        var lenMarker = raw.LastIndexOf(" Len: ", StringComparison.Ordinal);
        return lenMarker > jsonStart ? raw[jsonStart..lenMarker] : raw[jsonStart..];
    }

    private MeshcomMessage? ParseMessage(string raw)
    {
        raw = UnwrapExtMessage(raw);

        if (!raw.StartsWith('{'))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp) ||
                !root.TryGetProperty("src",  out var srcProp))
                return null;

            var msgType          = typeProp.GetString();
            var isPositionBeacon = msgType == "pos";
            var isTelemetry      = msgType == "tele";

            // Handle "msg", "pos", and "tele" types; ignore everything else
            if (msgType != "msg" && msgType != "pos" && msgType != "tele")
                return null;

            var src = srcProp.GetString() ?? string.Empty;

            // "msg" requires dst + msg fields; "pos" may omit them; "tele" has neither
            string dst = "*";
            string msg = string.Empty;
            if (msgType == "msg")
            {
                if (!root.TryGetProperty("dst", out var dstProp) ||
                    !root.TryGetProperty("msg", out var msgProp))
                    return null;
                dst = dstProp.GetString() ?? string.Empty;
                msg = msgProp.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("dst", out var dstProp2))
            {
                dst = dstProp2.GetString() ?? "*";
            }

            // For relayed messages ("OE1XAR-62,DB0TAW-13,..."), use the first callsign as sender;
            // preserve the full path for display.
            var commaIdx  = src.IndexOf(',');
            var sender    = commaIdx >= 0 ? src[..commaIdx] : src;
            var relayPath = commaIdx >= 0 ? src : null;

            // Extract sequence number from {NNN} before stripping it
            string? seqNum = null;
            if (!isPositionBeacon && !isTelemetry)
            {
                var seqMatch = SequenceNumberPattern().Match(msg);
                if (seqMatch.Success)
                    seqNum = seqMatch.Groups[1].Value;
                msg = TrailingSequencePattern().Replace(msg, string.Empty);
            }

            // Detect APRS-style ACK: "NOCALL-2 :ack187" (callsign may be space-padded to 9 chars)
            var isAck = !isPositionBeacon && AckPattern().IsMatch(msg.Trim());

            // For ACK messages extract the sequence number from the :ackNNN part
            if (isAck)
            {
                var ackSeqMatch = AckSequencePattern().Match(msg);
                if (ackSeqMatch.Success)
                    seqNum = ackSeqMatch.Groups[1].Value;
            }

            // Detect MeshCom time-sync broadcasts: "{CET}2026-04-07 18:11:58"
            var isTimeSync = !isAck && !isPositionBeacon && !isTelemetry && TimeSyncPattern().IsMatch(msg);

            // src_type:"node"
            var srcType      = root.TryGetProperty("src_type", out var srcTypeProp) ? srcTypeProp.GetString() : "lora";
            var isNodePacket = string.Equals(srcType, "node", StringComparison.OrdinalIgnoreCase);

            int? rssiRaw = (!isNodePacket && root.TryGetProperty("rssi", out var rssiProp)) ? rssiProp.GetInt32() : null;
            int?    rssi = rssiRaw is < 0 ? rssiRaw : null;   // 0 or positive = invalid/unset firmware default
            double? snr  = (!isNodePacket && root.TryGetProperty("snr",  out var snrProp))  ? snrProp.GetDouble() : null;

            // ── msg_id ───────────────────────────────────────────────────────
            string? msgId = root.TryGetProperty("msg_id", out var msgIdProp) ? msgIdProp.GetString() : null;

            // ── Hardware, firmware, battery ──────────────────────────────────
            int? hwId = root.TryGetProperty("hw_id", out var hwProp) && hwProp.ValueKind == JsonValueKind.Number
                ? hwProp.GetInt32() : null;

            int? battery = root.TryGetProperty("batt", out var battProp) && battProp.ValueKind == JsonValueKind.Number
                ? battProp.GetInt32() : null;

            // "firmware" can be integer (35) or string ("4.35")
            string? rawFw = null;
            if (root.TryGetProperty("firmware", out var fwProp))
                rawFw = fwProp.ValueKind == JsonValueKind.String ? fwProp.GetString() : fwProp.GetInt32().ToString();
            string? fwSub   = root.TryGetProperty("fw_sub", out var fwSubProp) ? fwSubProp.GetString() : null;
            string? firmware = MeshcomLookup.FormatFirmware(rawFw, fwSub);

            // ── GPS coordinates ──────────────────────────────────────────────
            // MeshCom node format uses separate direction fields:
            //   "lat":50.8515, "lat_dir":"N",  "long":9.1075, "long_dir":"E"
            // Some LoRa-relayed packets use signed "lat"/"lon" without direction.
            double? lat = null;
            double? lon = null;
            int?    alt = null;

            if (root.TryGetProperty("lat", out var latProp) && latProp.ValueKind == JsonValueKind.Number)
            {
                lat = latProp.GetDouble();
                if (root.TryGetProperty("lat_dir", out var latDirProp) &&
                    latDirProp.GetString()?.Equals("S", StringComparison.OrdinalIgnoreCase) == true)
                    lat = -lat;
            }

            // Longitude: MeshCom node uses "long"; LoRa-relayed packets may use "lon"
            if (root.TryGetProperty("long", out var longProp) && longProp.ValueKind == JsonValueKind.Number)
            {
                lon = longProp.GetDouble();
                if (root.TryGetProperty("long_dir", out var longDirProp) &&
                    longDirProp.GetString()?.Equals("W", StringComparison.OrdinalIgnoreCase) == true)
                    lon = -lon;
            }
            else if (root.TryGetProperty("lon", out var lonProp) && lonProp.ValueKind == JsonValueKind.Number)
            {
                lon = lonProp.GetDouble();
                if (root.TryGetProperty("lon_dir", out var lonDirProp) &&
                    lonDirProp.GetString()?.Equals("W", StringComparison.OrdinalIgnoreCase) == true)
                    lon = -lon;
            }

            if (root.TryGetProperty("alt", out var altProp) && altProp.ValueKind == JsonValueKind.Number)
            {
                // MeshCom uses APRS convention: altitude in feet -> convert to metres
                alt = (int)Math.Round(altProp.GetInt32() * 0.3048);
            }

            // 0°N 0°E (null island) = no GPS fix
            if (lat is 0.0 && lon is 0.0) { lat = null; lon = null; alt = null; }

            // ── Telemetry fields ─────────────────────────────────────────────
            double? temp1    = null;
            double? temp2    = null;
            double? humidity = null;
            double? pressure = null;
            if (isTelemetry)
            {
                if (root.TryGetProperty("temp1", out var t1) && t1.ValueKind == JsonValueKind.Number) temp1    = t1.GetDouble();
                if (root.TryGetProperty("temp2", out var t2) && t2.ValueKind == JsonValueKind.Number) temp2    = t2.GetDouble();
                if (root.TryGetProperty("hum",   out var hm) && hm.ValueKind == JsonValueKind.Number) humidity = hm.GetDouble();
                // Prefer qnh (sea-level) over qfe (station pressure)
                if (root.TryGetProperty("qnh", out var qnh) && qnh.ValueKind == JsonValueKind.Number && qnh.GetDouble() > 0)
                    pressure = qnh.GetDouble();
                else if (root.TryGetProperty("qfe", out var qfe) && qfe.ValueKind == JsonValueKind.Number && qfe.GetDouble() > 0)
                    pressure = qfe.GetDouble();
            }

            return new MeshcomMessage
            {
                From             = sender,
                To               = dst,
                Text             = msg,
                IsOutgoing       = false,
                RawData          = raw,
                Rssi             = rssi,
                Snr              = snr,
                Latitude         = lat,
                Longitude        = lon,
                Altitude         = alt,
                IsPositionBeacon = isPositionBeacon,
                IsTelemetry      = isTelemetry,
                IsAck            = isAck,
                IsTimeSync       = isTimeSync,
                MsgId            = msgId,
                SequenceNumber   = seqNum,
                RelayPath        = relayPath,
                SrcType          = srcType,
                HwId             = hwId,
                Battery          = battery,
                Firmware         = firmware,
                Temp1            = temp1,
                Temp2            = temp2,
                Humidity         = humidity,
                Pressure         = pressure,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON message: {Data}", raw);
            return null;
        }
    }
}
