using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MeshcomWebClient.Models;

namespace MeshcomWebClient.Services;

/// <summary>
/// Background service that handles UDP communication with a MeshCom device.
/// Listens for incoming messages and provides a method to send messages.
/// 
/// MeshCom EXTUDP JSON format:
///   RX chat : {"src_type":"lora","type":"msg","src":"DH1FR-1","dst":"DH1FR-2","msg":"Hello{034","rssi":-95,"snr":12,...}
///   RX pos  : {"src_type":"node","type":"pos","src":"DH1FR-2","lat":50.8515,"lat_dir":"N","long":9.1075,"long_dir":"E","alt":827,...}
///   TX chat : {"type":"msg","dst":"DH1FR-1","msg":"Hello"}
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

    public MeshcomUdpService(
        ILogger<MeshcomUdpService> logger,
        IOptionsMonitor<MeshcomSettings> settings,
        ChatService chatService)
    {
        _logger = logger;
        _chatService = chatService;
        _settings = settings.CurrentValue;
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
                            _logger.LogDebug("Skipping node echo from {From}", message.From);
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

    /// <summary>
    /// Send a text message to the MeshCom device via UDP.
    /// </summary>
    public async Task SendMessageAsync(string destination, string text)
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
                To = destination,
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

            // Only handle "msg" (chat) and "pos" (position beacon) types
            if (msgType != "msg" && msgType != "pos")
                return null;

            var src = srcProp.GetString() ?? string.Empty;

            // "msg" requires dst + msg fields; "pos" may omit them
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

            // For relayed messages ("OE1XAR-62,DB0TAW-13,..."), use the first callsign as sender
            var commaIdx = src.IndexOf(',');
            var sender   = commaIdx >= 0 ? src[..commaIdx] : src;

            // Strip trailing sequence marker like {034, {333
            if (!isPositionBeacon)
                msg = TrailingSequencePattern().Replace(msg, string.Empty);

            // src_type:"node" = local device packet; rssi/snr are 0 and not meaningful
            var srcType      = root.TryGetProperty("src_type", out var srcTypeProp) ? srcTypeProp.GetString() : "lora";
            var isNodePacket = string.Equals(srcType, "node", StringComparison.OrdinalIgnoreCase);

            int?    rssi = (!isNodePacket && root.TryGetProperty("rssi", out var rssiProp)) ? rssiProp.GetInt32()  : null;
            double? snr  = (!isNodePacket && root.TryGetProperty("snr",  out var snrProp))  ? snrProp.GetDouble() : null;

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
                IsPositionBeacon = isPositionBeacon
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse JSON message: {Data}", raw);
            return null;
        }
    }
}
