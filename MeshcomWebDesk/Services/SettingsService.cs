using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
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
    private readonly string _overridePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly IDataProtector _protector;

    public SettingsService(IConfiguration config, ILogger<SettingsService> logger,
                           IDataProtectionProvider dataProtection)
    {
        var dataPath  = config.GetValue<string>($"{MeshcomSettings.SectionName}:DataPath")
                        ?? Path.GetTempPath();
        Directory.CreateDirectory(dataPath);
        _overridePath = Path.Combine(dataPath, "appsettings.override.json");
        _logger       = logger;
        _protector    = dataProtection.CreateProtector("MeshcomWebDesk.Settings.v1");
    }

    /// <summary>Encrypts a non-empty value with the Data Protection API and prepends "dp:".</summary>
    private string Encrypt(string value) =>
        string.IsNullOrEmpty(value) ? value : "dp:" + _protector.Protect(value);

    public async Task SaveMeshcomSettingsAsync(MeshcomSettings s)
    {
        var root = new JsonObject
        {
            ["Meshcom"] = new JsonObject
            {
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
                ["BotEnabled"]         = s.BotEnabled,
                ["BotCommands"]        = new JsonArray(s.BotCommands.Select(c => (JsonNode?)new JsonObject
                {
                    ["Name"]        = c.Name,
                    ["Response"]    = c.Response,
                    ["Description"] = c.Description
                }).ToArray()),
                ["BeaconEnabled"]       = s.BeaconEnabled,
                ["BeaconGroup"]         = s.BeaconGroup,
                ["BeaconText"]          = s.BeaconText,
                ["BeaconIntervalHours"] = s.BeaconIntervalHours,
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
                ["TelemetryApiEnabled"] = s.TelemetryApiEnabled,
                ["TelemetryApiKey"]     = Encrypt(s.TelemetryApiKey),
                ["Language"]            = s.Language,
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
                ["FrequencyMhz"]   = s.FrequencyMhz,
                ["SystemMarginDb"] = s.SystemMarginDb
            }
        };

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_overridePath, output + Environment.NewLine, Encoding.UTF8);
        _logger.LogInformation("Settings saved to {Path}", _overridePath);
    }
}
