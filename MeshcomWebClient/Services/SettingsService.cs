using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeshcomWebClient.Models;

namespace MeshcomWebClient.Services;

/// <summary>
/// Writes user-configured Meshcom settings to an override file in DataPath
/// (appsettings.override.json). This file is loaded by Program.cs as an additional
/// configuration source layered on top of appsettings.json, which means it works
/// even when appsettings.json is mounted read-only in Docker.
/// ASP.NET Core's built-in file-watcher reloads IConfiguration automatically after saving.
/// </summary>
public class SettingsService
{
    private readonly string _overridePath;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IConfiguration config, ILogger<SettingsService> logger)
    {
        var dataPath  = config.GetValue<string>($"{MeshcomSettings.SectionName}:DataPath")
                        ?? Path.GetTempPath();
        Directory.CreateDirectory(dataPath);
        _overridePath = Path.Combine(dataPath, "appsettings.override.json");
        _logger = logger;
    }

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
                ["DataPath"]            = s.DataPath,
                ["AutoReplyEnabled"]    = s.AutoReplyEnabled,
                ["AutoReplyText"]       = s.AutoReplyText,
                ["BeaconEnabled"]       = s.BeaconEnabled,
                ["BeaconGroup"]         = s.BeaconGroup,
                ["BeaconText"]          = s.BeaconText,
                ["BeaconIntervalHours"] = s.BeaconIntervalHours,
                ["TelemetryEnabled"]       = s.TelemetryEnabled,
                ["TelemetryFilePath"]      = s.TelemetryFilePath,
                ["TelemetryGroup"]         = s.TelemetryGroup,
                ["TelemetryIntervalHours"] = s.TelemetryIntervalHours,
                ["TelemetryMapping"]       = new JsonArray(s.TelemetryMapping.Select(m => (JsonNode?)new JsonObject
                {
                    ["JsonKey"]  = m.JsonKey,
                    ["Label"]    = m.Label,
                    ["Unit"]     = m.Unit,
                    ["Decimals"] = m.Decimals
                }).ToArray()),
                ["TelemetryApiEnabled"] = s.TelemetryApiEnabled,
                ["TelemetryApiKey"]     = s.TelemetryApiKey,
                ["Language"]            = s.Language
            }
        };

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_overridePath, output + Environment.NewLine, Encoding.UTF8);
        _logger.LogInformation("Settings saved to {Path}", _overridePath);
    }
}
