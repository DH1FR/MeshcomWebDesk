using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Writes user-configured Meshcom settings to an override file in DataPath
/// (appsettings.override.json). This file is loaded by Program.cs as an additional
/// configuration source layered on top of appsettings.json, which means it works
/// even when appsettings.json is mounted read-only in Docker.
/// ASP.NET Core's built-in file-watcher reloads IConfiguration automatically after saving.
/// Sensitive fields (connection strings, tokens, passwords) are encrypted using the
/// ASP.NET Core Data Protection API before writing (prefix <c>"dp:"</c>).
/// </summary>
public class SettingsService
{
    private readonly string _dataPath;
    private readonly string _overridePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly ISettingsProtector _protector;
    private readonly IConfigurationRoot? _configRoot;

    public string EffectiveDataPath => _dataPath;

    public SettingsService(IConfiguration config, ILogger<SettingsService> logger,
                           ISettingsProtector protector)
    {
        var dataPath  = config.GetValue<string>($"{MeshcomSettings.SectionName}:DataPath")
                        ?? Path.GetTempPath();
        Directory.CreateDirectory(dataPath);
        _dataPath     = Path.GetFullPath(dataPath);
        _overridePath = Path.Combine(_dataPath, "appsettings.override.json");
        _logger       = logger;
        _protector    = protector;
        _configRoot   = config as IConfigurationRoot;
    }

    /// <summary>
    /// Probes whether <paramref name="directory"/> can be created and written to.
    /// Returns null on success, or a human-readable error message on failure.
    /// </summary>
    /// <summary>
    /// Checks whether <paramref name="path"/> is syntactically valid for the current OS.
    /// Returns null when valid (including empty/null, which means "use default").
    /// </summary>
    public static string? ValidatePathSyntax(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            Path.GetFullPath(path);
            return null;
        }
        catch (ArgumentException)
        {
            return "Ungültige Zeichen im Pfad.";
        }
        catch (NotSupportedException)
        {
            return "Ungültiger Pfad (Doppelpunkt an falscher Position).";
        }
        catch (PathTooLongException)
        {
            return "Pfad ist zu lang.";
        }
    }

    /// <summary>
    /// Probes whether <paramref name="directory"/> is writable by writing a temp file.
    /// When <paramref name="createIfMissing"/> is true the directory is created first
    /// (use for LogPath, where Serilog does the same on startup).
    /// Returns null on success, or a human-readable error string on failure.
    /// </summary>
    public static async Task<string?> CheckWritabilityAsync(string directory, string context,
                                                            bool createIfMissing = false)
    {
        try
        {
            if (createIfMissing) Directory.CreateDirectory(directory);
            var testFile = Path.Combine(directory, ".write-test");
            await File.WriteAllTextAsync(testFile, "ok");
            File.Delete(testFile);
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return $"{context}: Verzeichnis nicht gefunden – {directory}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"{context}: Kein Schreibzugriff – {directory}";
        }
        catch (Exception ex)
        {
            return $"{context}: {ex.Message} ({directory})";
        }
    }

    /// <summary>
    /// Encrypts a non-empty plaintext value with AES-256-GCM.
    /// If the value is already encrypted (aes: / dp: prefix) it is returned unchanged
    /// to prevent double-encryption of stale model values.
    /// </summary>
    private string Encrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.StartsWith("aes:", StringComparison.Ordinal) ||
            value.StartsWith("dp:",  StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "SettingsService.Encrypt: Wert hat bereits Verschlüsselungs-Prefix '{Prefix}' – " +
                "wird unverändert gespeichert. Bitte Wert neu eingeben.",
                value[..4]);
            return value;
        }
        return _protector.Encrypt(value);
    }

    /// <summary>
    /// Encrypts a new plaintext value. If the new value is empty, reads the existing
    /// encrypted value from the override file and keeps it unchanged.
    /// This prevents overwriting a valid key with an empty value when the UI omits
    /// pre-filling password/key fields for security reasons.
    /// </summary>
    private string EncryptOrKeepExisting(string newValue, string section, string key)
    {
        if (!string.IsNullOrEmpty(newValue))
            return Encrypt(newValue);

        // New value is empty – keep the existing encrypted value from disk
        try
        {
            if (File.Exists(_overridePath))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_overridePath));
                if (doc.RootElement.TryGetProperty("Meshcom", out var meshcom) &&
                    meshcom.TryGetProperty(section, out var sec) &&
                    sec.TryGetProperty(key, out var existing))
                {
                    var existing_val = existing.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(existing_val))
                    {
                        _logger.LogDebug("SettingsService: {Section}.{Key} leer – vorhandener Wert wird beibehalten.", section, key);
                        return existing_val;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SettingsService: Konnte vorhandenen Wert für {Section}.{Key} nicht lesen.", section, key);
        }
        return string.Empty;
    }

    public async Task SaveMeshcomSettingsAsync(MeshcomSettings s)
    {
        var root = new JsonObject
        {
            ["Meshcom"] = new JsonObject
            {
                ["Nodes"] = new JsonArray(s.Nodes.Select(n => (JsonNode?)new JsonObject
                {
                    ["Id"]                   = n.Id.ToString(),
                    ["Name"]                 = n.Name,
                    ["Callsign"]             = n.Callsign,
                    ["DeviceIp"]             = n.DeviceIp,
                    ["DevicePort"]           = n.DevicePort,
                    ["ListenIp"]             = n.ListenIp,
                    ["ListenPort"]           = n.ListenPort,
                    ["IsPrimary"]            = n.IsPrimary,
                    ["Enabled"]              = n.Enabled,
                    ["Transport"]            = n.Transport.ToString(),
                    ["KissPort"]             = n.KissPort,
                    ["TelnetCertThumbprint"] = n.TelnetCertThumbprint,
                    ["TelnetPassword"]       = Encrypt(n.TelnetPassword),
                    ["ConsoleLogEnabled"]    = n.ConsoleLogEnabled
                }).ToArray()),
                ["ListenIp"]            = s.ListenIp,
                ["ListenPort"]          = s.ListenPort,
                ["DeviceIp"]            = s.DeviceIp,
                ["DevicePort"]          = s.DevicePort,
                ["MyCallsign"]          = s.MyCallsign,
                ["LogPath"]             = s.LogPath,
                ["LogRetainDays"]       = s.LogRetainDays,
                ["LogUdpTraffic"]       = s.LogUdpTraffic,
                ["MonitorMaxMessages"]  = s.MonitorMaxMessages,
                ["GroupFilterEnabled"]  = s.GroupFilterEnabled,
                ["Groups"]              = new JsonArray(s.Groups.Select(g => (JsonNode?)JsonValue.Create(g)).ToArray()),
                ["WatchCallsigns"]      = new JsonArray(s.WatchCallsigns.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
                ["WatchOnMessage"]      = s.WatchOnMessage,
                ["WatchOnPosition"]     = s.WatchOnPosition,
                ["WatchOnTelemetry"]    = s.WatchOnTelemetry,
                ["WatchOnAck"]          = s.WatchOnAck,
                ["WatchAlertMinutes"]   = s.WatchAlertMinutes,
                ["DataPath"]            = s.DataPath,
                ["TimeOffsetHours"]     = s.TimeOffsetHours,
                ["AutoReplyEnabled"]    = s.AutoReplyEnabled,
                ["AutoReplyText"]       = s.AutoReplyText,
                ["ReplyDelaySeconds"]   = s.ReplyDelaySeconds,
                ["BotEnabled"]                  = s.BotEnabled,
                ["BotCommands"]                 = new JsonArray(s.BotCommands.Select(c => (JsonNode?)new JsonObject
                {
                    ["Name"]             = c.Name,
                    ["Response"]         = c.Response,
                    ["Description"]      = c.Description,
                    ["IsExternal"]       = c.IsExternal,
                    ["ExternalFileName"] = c.ExternalFileName,
                    ["TimeoutSeconds"]   = c.TimeoutSeconds,
                }).ToArray()),
                ["BotExternalCommandsPath"]     = s.BotExternalCommandsPath,
                ["BeaconEnabled"]       = s.BeaconEnabled,
                ["BeaconGroup"]         = s.BeaconGroup,
                ["BeaconText"]          = s.BeaconText,
                ["BeaconIntervalHours"] = s.BeaconIntervalHours,
                ["CalendarBeacons"]     = new JsonArray(s.CalendarBeacons.Select(e => (JsonNode?)new JsonObject
                {
                    ["Id"]                = e.Id,
                    ["Title"]             = e.Title,
                    ["Enabled"]           = e.Enabled,
                    ["Group"]             = e.Group,
                    ["Text"]              = e.Text,
                    ["RecurrenceType"]    = e.RecurrenceType.ToString(),
                    ["EventDayOfWeek"]    = e.EventDayOfWeek.ToString(),
                    ["EventDayOfMonth"]   = e.EventDayOfMonth,
                    ["WeekdayOrdinal"]    = e.WeekdayOrdinal,
                    ["EventTime"]         = e.EventTime,
                    ["ReferenceDate"]     = e.ReferenceDate,
                    ["AnnounceLeadTimes"] = e.AnnounceLeadTimes,
                    ["AnnounceAtEvent"]   = e.AnnounceAtEvent,
                    ["IsExternal"]        = e.IsExternal,
                    ["ExternalFileName"]  = e.ExternalFileName,
                    ["TimeoutSeconds"]    = e.TimeoutSeconds
                }).ToArray()),
                ["TelemetryEnabled"]       = s.TelemetryEnabled,
                ["TelemetryFilePath"]      = s.TelemetryFilePath,
                ["TelemetryGroup"]         = s.TelemetryGroup,
                ["TelemetryScheduleHours"] = s.TelemetryScheduleHours,
                ["TelemetryMapping"]       = new JsonArray(s.TelemetryMapping.Select(m => (JsonNode?)new JsonObject
                {
                    ["JsonKey"]     = m.JsonKey,
                    ["Label"]       = m.Label,
                    ["Unit"]        = m.Unit,
                    ["Decimals"]    = m.Decimals,
                    ["WeatherRole"] = m.WeatherRole
                }).ToArray()),
                ["TelemetryApiEnabled"]              = s.TelemetryApiEnabled,
                ["TelemetryApiKey"]                  = Encrypt(s.TelemetryApiKey),
                ["TelemetryExtUdpEnabled"]            = s.TelemetryExtUdpEnabled,
                ["TelemetryExtUdpMinIntervalMinutes"] = s.TelemetryExtUdpMinIntervalMinutes,
                ["Language"]            = s.Language,
                ["Appearance"] = new JsonObject
                {
                    ["ActiveTheme"]  = s.Appearance.ActiveTheme,
                    ["CustomThemes"] = new JsonArray(s.Appearance.CustomThemes.Select(t => (JsonNode?)new JsonObject
                    {
                        ["Name"]    = t.Name,
                        ["BasedOn"] = t.BasedOn,
                        ["Colors"]  = new JsonObject(t.Colors.Select(c =>
                                          new KeyValuePair<string, JsonNode?>(c.Key, JsonValue.Create(c.Value))))
                    }).ToArray())
                },
                ["Database"]            = new JsonObject
                {
                    ["Provider"]              = s.Database.Provider,
                    ["MySqlConnectionString"] = Encrypt(s.Database.MySqlConnectionString),
                    ["MySqlTableName"]        = s.Database.MySqlTableName,
                    ["InfluxUrl"]             = s.Database.InfluxUrl,
                    ["InfluxToken"]           = Encrypt(s.Database.InfluxToken),
                    ["InfluxOrg"]             = s.Database.InfluxOrg,
                    ["InfluxBucket"]          = s.Database.InfluxBucket,
                    ["LogInserts"]            = s.Database.LogInserts
                },
                ["Webhook"] = new JsonObject
                {
                    ["Enabled"]     = s.Webhook.Enabled,
                    ["Url"]         = s.Webhook.Url,
                    ["OnMessage"]   = s.Webhook.OnMessage,
                    ["OnPosition"]  = s.Webhook.OnPosition,
                    ["OnTelemetry"] = s.Webhook.OnTelemetry
                },
                ["Mqtt"] = new JsonObject
                {
                    ["Enabled"]          = s.Mqtt.Enabled,
                    ["Host"]             = s.Mqtt.Host,
                    ["Port"]             = s.Mqtt.Port,
                    ["ClientId"]         = s.Mqtt.ClientId,
                    ["Username"]         = s.Mqtt.Username,
                    ["Password"]         = Encrypt(s.Mqtt.Password),
                    ["UseTls"]           = s.Mqtt.UseTls,
                    ["TopicPrefix"]      = s.Mqtt.TopicPrefix,
                    ["PublishMessage"]   = s.Mqtt.PublishMessage,
                    ["PublishPosition"]  = s.Mqtt.PublishPosition,
                    ["PublishTelemetry"] = s.Mqtt.PublishTelemetry,
                    ["SubscribeEnabled"] = s.Mqtt.SubscribeEnabled,
                    ["LogRequests"]      = s.Mqtt.LogRequests
                },
                ["KissHub"] = new JsonObject
                {
                    ["Enabled"] = s.KissHub.Enabled,
                    ["Port"]    = s.KissHub.Port,
                    ["NodeId"]  = s.KissHub.NodeId?.ToString(),
                    ["BindLan"] = s.KissHub.BindLan,
                },
                ["Qrz"] = new JsonObject
                {
                    ["Enabled"]         = s.Qrz.Enabled,
                    ["Username"]        = s.Qrz.Username,
                    ["Password"]        = Encrypt(s.Qrz.Password),
                    ["LogRequests"]     = s.Qrz.LogRequests,
                    ["CacheMaxAgeDays"] = s.Qrz.CacheMaxAgeDays
                },
                ["Ai"] = new JsonObject
                {
                    ["Enabled"]          = s.Ai.Enabled,
                    ["Provider"]         = s.Ai.Provider,
                    ["ApiKey"]           = Encrypt(s.Ai.ApiKey),
                    ["Model"]            = s.Ai.Model,
                    ["AzureEndpoint"]    = s.Ai.AzureEndpoint,
                    ["AzureApiVersion"]  = s.Ai.AzureApiVersion,
                    ["ThresholdDays"]    = s.Ai.ThresholdDays,
                    ["SummaryDays"]      = s.Ai.SummaryDays,
                    ["MaxMessages"]      = s.Ai.MaxMessages,
                    ["LogRequests"]      = s.Ai.LogRequests
                },
                ["QuickTexts"] = new JsonArray(s.QuickTexts.Select(q => (JsonNode?)new JsonObject
                {
                    ["Label"] = q.Label,
                    ["Text"]  = q.Text
                }).ToArray()),
                ["GroupLabels"] = new JsonArray(s.GroupLabels.Select(g => (JsonNode?)new JsonObject
                {
                    ["Group"]      = g.Group,
                    ["ShortLabel"] = g.ShortLabel,
                    ["Label"]      = g.Label
                }).ToArray()),
                ["MhMaxAgeHours"]  = s.MhMaxAgeHours,
                ["TxPowerDbm"]              = s.TxPowerDbm,
                ["CableType"]               = s.CableType,
                ["CableLengthM"]            = s.CableLengthM,
                ["CustomCableLossDbPer10m"] = s.CustomCableLossDbPer10m,
                ["AntennaGainDbi"]          = s.AntennaGainDbi,
                ["AntennaType"]             = s.AntennaType,
                ["AntennaHeightM"]          = s.AntennaHeightM,
                ["FrequencyMhz"]            = s.FrequencyMhz,
                ["SystemMarginDb"]          = s.SystemMarginDb,
                ["OwnMessagesAlignLeft"]    = s.OwnMessagesAlignLeft,
                ["TxCooldownSeconds"]       = s.TxCooldownSeconds,
                ["GatewayHighlightEnabled"] = s.GatewayHighlightEnabled,
                ["GatewayServer"]           = s.GatewayServer,
                ["GatewaySources"] = new JsonArray(s.GatewaySources.Select(g => (JsonNode?)new JsonObject
                {
                    ["Name"]    = g.Name,
                    ["Url"]     = g.Url,
                    ["Enabled"] = g.Enabled
                }).ToArray()),
                ["TelnetEnabled"]           = s.TelnetEnabled,
                ["ConsoleMode"]             = s.ConsoleMode,
                ["TelnetPort"]              = s.TelnetPort,
                ["TelnetPassword"]          = Encrypt(s.TelnetPassword),
                ["TelnetCertThumbprint"]    = s.TelnetCertThumbprint,
                ["SerialPortName"]          = s.SerialPortName,
                ["SerialBaudRate"]          = s.SerialBaudRate,
                ["ConsoleLogEnabled"]       = s.ConsoleLogEnabled,
                ["WeatherApi"] = new JsonObject
                {
                    ["Provider"]            = s.WeatherApi.Provider.ToString(),
                    ["ApiKey"]              = EncryptOrKeepExisting(s.WeatherApi.ApiKey, "WeatherApi", "ApiKey"),
                    ["StationId"]           = s.WeatherApi.StationId,
                    ["PollIntervalMinutes"] = s.WeatherApi.PollIntervalMinutes,
                    ["LogRequests"]         = s.WeatherApi.LogRequests,
                },
                ["LicenseToken"] = s.LicenseToken
            }
        };

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        try
        {
            await File.WriteAllTextAsync(_overridePath, output + Environment.NewLine, Encoding.UTF8);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Kein Schreibzugriff auf die Einstellungsdatei '{_overridePath}'. " +
                $"Bitte sicherstellen, dass der Prozess Schreibrechte auf den DataPath-Ordner hat " +
                $"(Docker: Volume korrekt gemountet? Windows: Ordnerberechtigungen prüfen).");
        }
        catch (DirectoryNotFoundException)
        {
            throw new InvalidOperationException(
                $"Verzeichnis '{Path.GetDirectoryName(_overridePath)}' nicht gefunden. " +
                $"DataPath prüfen – der Ordner muss vorhanden oder vom Prozess erstellbar sein.");
        }
        // inotify on Linux doesn't always fire for in-place file writes, so we force reload
        // instead of relying on reloadOnChange to propagate changes to IOptionsMonitor.
        _configRoot?.Reload();
        _logger.LogInformation("Settings saved to {Path}", _overridePath);
    }
}
