using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebClient.Helpers;
using MeshcomWebClient.Models;

namespace MeshcomWebClient.Services;

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
    private readonly MeshcomSettings _settings;
    private UdpClient? _udpClient;

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

    public MeshcomUdpService(
        ILogger<MeshcomUdpService> logger,
        IOptionsMonitor<MeshcomSettings> settings,
        ChatService chatService)
    {
        _logger    = logger;
        _chatService = chatService;
        _settings  = settings.CurrentValue;

        _chatService.OnNewDirectTab += callsign => _ = SendAutoReplyAsync(callsign);
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
                            _chatService.AddRawMessage(message);
                        }
                        else if (message.IsAck)
                        {
                            // APRS ACK – monitor only, no chat tab; mark matching message as delivered
                            Status.RxCount++;
                            Status.LastRxTime = message.Timestamp;
                            Status.LastRxFrom = message.From;
                            NotifyStatusChange();
                            if (message.SequenceNumber != null)
                                _chatService.MarkMessageAcknowledged(message.SequenceNumber);
                            _chatService.AddRawMessage(message);
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
            var json = JsonSerializer.Serialize(new { type = "info", src = _settings.MyCallsign, dst = "*", msg = "info" });
            var bytes = Encoding.UTF8.GetBytes(json);
            var remoteEp = new IPEndPoint(IPAddress.Parse(_settings.DeviceIp), _settings.DevicePort);

            await _udpClient.SendAsync(bytes, bytes.Length, remoteEp);
            _logger.LogInformation("UDP registration packet sent to {DeviceIp}:{DevicePort}", _settings.DeviceIp, _settings.DevicePort);
            if (_settings.LogUdpTraffic)
                _logger.LogInformation("[UDP-TX] {Remote} {Data}", remoteEp, json);
            Status.IsRegistered = true;
            NotifyStatusChange();
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

        _logger.LogInformation("Auto-reply to new contact {Callsign}", callsign);
        return SendMessageAsync(callsign, _settings.AutoReplyText);
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

            // src_type:"node" = local device packet; rssi/snr are 0 and not meaningful
            var srcType      = root.TryGetProperty("src_type", out var srcTypeProp) ? srcTypeProp.GetString() : "lora";
            var isNodePacket = string.Equals(srcType, "node", StringComparison.OrdinalIgnoreCase);

            int?    rssi = (!isNodePacket && root.TryGetProperty("rssi", out var rssiProp)) ? rssiProp.GetInt32()  : null;
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
