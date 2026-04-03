using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeshcomWebClient.Models;

namespace MeshcomWebClient.Services;

/// <summary>
/// Reads and writes the Meshcom section of appsettings.json.
/// ASP.NET Core's built-in file-watcher reloads IConfiguration automatically after saving.
/// Note: running services that captured settings at construction time require an application restart.
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IWebHostEnvironment env, ILogger<SettingsService> logger)
    {
        _settingsPath = Path.Combine(env.ContentRootPath, "appsettings.json");
        _logger = logger;
    }

    public async Task SaveMeshcomSettingsAsync(MeshcomSettings s)
    {
        var json = await File.ReadAllTextAsync(_settingsPath);
        var root = JsonNode.Parse(json)!.AsObject();

        root["Meshcom"] = new JsonObject
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
            ["BeaconIntervalHours"] = s.BeaconIntervalHours
        };

        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_settingsPath, output + Environment.NewLine, Encoding.UTF8);
        _logger.LogInformation("Settings saved to {Path}", _settingsPath);
    }
}
