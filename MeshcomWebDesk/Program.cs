using MeshcomWebDesk.Components;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services;
using MeshcomWebDesk.Services.Bot;
using MeshcomWebDesk.Services.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;

// When running as a Windows Service the working directory defaults to System32.
// Must be set BEFORE CreateBuilder so the content root (appsettings.json, wwwroot, …)
// resolves to the executable's directory instead of System32.
Environment.CurrentDirectory = AppContext.BaseDirectory;

// ── --version argument ────────────────────────────────────────────────────────
if (args.Contains("--version"))
{
    var ver = System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "?";

    // Read license token and callsign directly from settings files (no DI needed).
    var licenseInfo = "Free unlicensed version";
    try
    {
        var baseConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();
        var versionDataPath = baseConfig.GetSection("MeshcomSettings").GetValue<string>("DataPath") ?? "data";
        var versionCfg = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine(versionDataPath, "appsettings.override.json"), optional: true)
            .Build();

        var callsign  = versionCfg.GetSection("MeshcomSettings").GetValue<string>("MyCallsign") ?? "";
        var token     = versionCfg.GetSection("MeshcomSettings:License").GetValue<string>("Token") ?? "";
        var callBase  = callsign.Contains('-') ? callsign[..callsign.IndexOf('-')] : callsign;

        if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(callBase))
        {
            // Quick token decode – extract Callsign field without full RSA validation.
            var parts = token.Split('.');
            if (parts.Length == 2)
            {
                var json   = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Callsign", out var cs))
                {
                    var tokenCall = cs.GetString() ?? "";
                    if (string.Equals(tokenCall, callBase, StringComparison.OrdinalIgnoreCase))
                        licenseInfo = $"Licensed for {tokenCall}";
                }
            }
        }
    }
    catch { /* settings not readable – show unlicensed */ }

    Console.WriteLine($"MeshCom WebDesk v{ver} – {licenseInfo}");
    return;
}

static void TryCreateDirectory(string path, string label)
{
    try
    {
        Directory.CreateDirectory(path);
    }
    catch (UnauthorizedAccessException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"""

  [FATAL] Cannot create directory for '{label}': {path}
          Access denied – the current user has no write permission for this path.

          Possible causes:
            • The path is a Linux/Docker-style path (e.g. /app) used on Windows.
              Fix: set  DataPath=./data  and  LogPath=./logs  in appsettings.json.
            • Docker: the container runs as a non-root user and /app is not writable.
              Fix: mount a writable volume, e.g.  -v ./data:/app/data
            • Direct install: the app directory is not writable by the service account.
              Fix: grant write permission on '{path}' to the service account.

          Original error: {ex.Message}
  """);
        Console.ResetColor();
        Environment.Exit(1);
    }
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "MeshcomWebDesk");

// ── Banner ────────────────────────────────────────────────────────────────────
var bannerVersion = System.Reflection.Assembly.GetExecutingAssembly()
    .GetName().Version?.ToString(3) ?? "?";
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"""

  ███╗   ███╗███████╗███████╗██╗  ██╗ ██████╗ ██████╗ ███╗   ███╗
  ████╗ ████║██╔════╝██╔════╝██║  ██║██╔════╝██╔═══██╗████╗ ████║
  ██╔████╔██║█████╗  ███████╗███████║██║     ██║   ██║██╔████╔██║
  ██║╚██╔╝██║██╔══╝  ╚════██║██╔══██║██║     ██║   ██║██║╚██╔╝██║
  ██║ ╚═╝ ██║███████╗███████║██║  ██║╚██████╗╚██████╔╝██║ ╚═╝ ██║
  ╚═╝     ╚═╝╚══════╝╚══════╝╚═╝  ╚═╝ ╚═════╝ ╚═════╝ ╚═╝     ╚═╝
    ██╗    ██╗███████╗██████╗ ██████╗ ███████╗███████╗██╗  ██╗
    ██║    ██║██╔════╝██╔══██╗██╔══██╗██╔════╝██╔════╝██║ ██╔╝
    ██║ █╗ ██║█████╗  ██████╔╝██║  ██║█████╗  ███████╗█████╔╝ 
    ██║███╗██║██╔══╝  ██╔══██╗██║  ██║██╔══╝  ╚════██║██╔═██╗ 
    ╚███╔███╔╝███████╗██████╔╝██████╔╝███████╗███████║██║  ██╗
     ╚══╝╚══╝ ╚══════╝╚═════╝ ╚═════╝ ╚══════╝╚══════╝╚═╝  ╚═╝
                         v{bannerVersion}
  https://github.com/DH1FR/MeshcomWebDesk

""");
Console.ResetColor();

// Read Meshcom settings early for log path configuration
var meshcomSection = builder.Configuration.GetSection(MeshcomSettings.SectionName);
var logPath = meshcomSection.GetValue<string>("LogPath") ?? @"C:\Temp\Logs";
var retainDays = meshcomSection.GetValue<int?>("LogRetainDays") ?? 30;

// Load user-written settings override from DataPath (writable volume in Docker).
// This file is created by SettingsService when the user saves settings via the UI.
// It is layered on top of appsettings.json so a Docker read-only mount still works.
var dataPath = meshcomSection.GetValue<string>("DataPath") ?? @"C:\Temp\MeshcomData";
TryCreateDirectory(dataPath, "DataPath");
var overrideFile = Path.Combine(dataPath, "appsettings.override.json");
builder.Configuration.AddJsonFile(overrideFile, optional: true, reloadOnChange: true);

TryCreateDirectory(logPath, "LogPath");

var botExternalCommandsPath = meshcomSection.GetValue<string>("BotExternalCommandsPath") ?? string.Empty;
if (!string.IsNullOrWhiteSpace(botExternalCommandsPath))
    TryCreateDirectory(botExternalCommandsPath, "BotExternalCommandsPath");

var logFile = Path.Combine(logPath, "MeshcomWebDesk-.log");

// Enable Static Web Assets when running directly from the build output (not published).
// Without this, a warning is logged and CSS/JS fingerprinting may not resolve correctly.
// For published binaries (GitHub Releases, Docker) this call is a no-op.
builder.WebHost.UseStaticWebAssets();

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    // Console: suppress DB-insert Information logs – they appear in the log file only.
    .WriteTo.Logger(lc => lc
        .MinimumLevel.Override("MeshcomWebDesk.Services.Database",  Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("MeshcomWebDesk.Services.QrzService", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console())
    .WriteTo.File(
        logFile,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainDays,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

// Bind MeshCom settings from configuration
builder.Services.Configure<MeshcomSettings>(meshcomSection);
// Register the resolved log path so services (e.g. ConsoleLogService) can use it
// independent of whether appsettings.override.json has overwritten LogPath with "".
builder.Services.AddSingleton(new ResolvedLogPath(logPath));
// Decrypt sensitive fields (connection strings, tokens, passwords) after loading.
// Values encrypted by SettingsService carry a "dp:" prefix; plain-text values pass through.
builder.Services.AddSingleton<IPostConfigureOptions<MeshcomSettings>,
                               DecryptMeshcomSettingsPostConfigure>();

// Persist Data Protection keys to disk so antiforgery tokens survive container restarts.
// Docker: override via DATAPROTECTION_KEYPATH env variable (e.g. /app/keys).
// Direct start: stored in dataPath/keys next to the other application data.
var keyPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYPATH") ?? Path.Combine(dataPath, "keys");
TryCreateDirectory(keyPath, "DATAPROTECTION_KEYPATH / keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(keyPath));

// Platform-independent AES-256-GCM encryption for sensitive settings fields.
// The key file (settings.key) is stored alongside the Data Protection keys and can
// be shared between Windows and Linux so that settings saved on one platform are
// readable on any other.
builder.Services.AddSingleton<ISettingsProtector>(sp =>
    new SettingsProtector(keyPath, sp.GetRequiredService<ILogger<SettingsProtector>>()));

// Register services
builder.Services.AddSingleton<QsoSummaryService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<IBotCommand, VersionCommand>();
builder.Services.AddSingleton<IBotCommand, TimeCommand>();
builder.Services.AddSingleton<IBotCommand, MhCommand>();
builder.Services.AddSingleton<IBotCommand, PingCommand>();
builder.Services.AddSingleton<IBotCommand, EchoCommand>();
builder.Services.AddSingleton<IBotCommand, MeshcomWebDesk.Services.Bot.WeatherStatusCommand>();
builder.Services.AddSingleton<ExternalProcessRunner>();
builder.Services.AddSingleton<BotCommandService>();
builder.Services.AddSingleton<MeshcomUdpService>();
builder.Services.AddSingleton<DataPersistenceService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<LanguageService>();
builder.Services.AddSingleton<MySqlMonitorSink>();
builder.Services.AddSingleton<InfluxDbMonitorSink>();
builder.Services.AddSingleton<MonitorSinkService>();
builder.Services.AddSingleton<IMonitorDataSink>(sp => sp.GetRequiredService<MonitorSinkService>());
builder.Services.AddSingleton<DatabaseSetupService>();
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddSingleton<QrzService>();
builder.Services.AddSingleton<MqttService>();
builder.Services.AddSingleton<UpdateCheckService>();
builder.Services.AddSingleton<ElevationService>();
builder.Services.AddSingleton<TelnetService>();
builder.Services.AddSingleton<SerialConsoleService>();
builder.Services.AddSingleton<HmacConsoleService>();
builder.Services.AddSingleton<ConsoleLogService>();
builder.Services.AddSingleton<ConsoleCommandHelperService>();
builder.Services.AddHttpClient("MeshcomGateway").ConfigurePrimaryHttpMessageHandler(
    () => new HttpClientHandler { AllowAutoRedirect = true });
builder.Services.AddHttpClient("WeatherApi", c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(
        () => new HttpClientHandler { AllowAutoRedirect = true });
builder.Services.AddSingleton<MeshcomWebDesk.Services.Weather.AwekasProvider>();
builder.Services.AddSingleton<MeshcomWebDesk.Services.Weather.WUndergroundProvider>();
builder.Services.AddSingleton<MeshcomWebDesk.Services.Weather.SimulationProvider>();
builder.Services.AddSingleton<MeshcomWebDesk.Services.AppLicenseService>();
builder.Services.AddSingleton<MeshcomWebDesk.Services.WeatherApiPollingService>();
builder.Services.AddSingleton<GatewayService>();
builder.Services.AddSingleton<NodeManager>();
builder.Services.AddSingleton<CalendarBeaconService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UpdateCheckService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayService>());
builder.Services.AddSingleton<IMeshcomSender>(sp => sp.GetRequiredService<MeshcomUdpService>());
builder.Services.AddSingleton<IMeshcomVariableExpander>(sp => sp.GetRequiredService<MeshcomUdpService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeshcomUdpService>());
builder.Services.AddSingleton<MeshcomWebDesk.Services.Kiss.KissClientService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeshcomWebDesk.Services.Kiss.KissClientService>());
builder.Services.AddSingleton<MeshcomWebDesk.Services.Kiss.KissHubService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeshcomWebDesk.Services.Kiss.KissHubService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DataPersistenceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeshcomWebDesk.Services.WeatherApiPollingService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CalendarBeaconService>());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // Default (10) is tuned for low-frequency apps. MeshcomWebDesk pushes frequent
        // UI updates (live chat, telemetry, tab badges), so a slow/throttled client
        // (real network instead of localhost, backgrounded browser tab) can fall behind
        // and exceed the default buffer, desyncing the circuit
        // (client-side "Cannot read properties of null (reading 'removeChild')" crash).
        options.MaxBufferedUnacknowledgedRenderBatches = 100;
    });

var app = builder.Build();

// Break circular dependency: ChatService ← MqttService ← IMeshcomSender ← MeshcomUdpService ← ChatService
var chatService = app.Services.GetRequiredService<ChatService>();
chatService.SetMqttService(app.Services.GetRequiredService<MqttService>());
chatService.SetNodeManager(app.Services.GetRequiredService<NodeManager>());

// Wire the KISS transport into the UDP service for TX routing (avoids a DI cycle).
app.Services.GetRequiredService<MeshcomUdpService>()
   .SetKissTransport(app.Services.GetRequiredService<MeshcomWebDesk.Services.Kiss.KissClientService>());

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// HTTPS redirect only when the LanHttps profile is active (port 5163).
// In standard HTTP-only mode (Docker, default launch) this must stay off –
// otherwise HTTP requests (e.g. from Home Assistant) get redirected to HTTPS.
if (app.Environment.IsEnvironment("LanHttps"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// POST /api/telemetry – accepts a JSON body from external sources (e.g. Home Assistant)
// and writes it to TelemetryFilePath so the TelemetryService can pick it up.
// Protected by an optional X-Api-Key header (configured via TelemetryApiKey).
app.MapPost("/api/telemetry", async (
    HttpContext        ctx,
    IOptionsMonitor<MeshcomSettings> settingsMonitor,
    ILogger<Program>   logger) =>
{
    var s = settingsMonitor.CurrentValue;

    if (!s.TelemetryApiEnabled)
        return Results.NotFound();

    // Option 1: Weather API und HTTP-API schließen sich gegenseitig aus
    if (s.WeatherApi?.Provider != MeshcomWebDesk.Models.WeatherProvider.None)
        return Results.Conflict(new { error = "HTTP-Telemetrie-API ist deaktiviert, solange die Wetter-API aktiv ist." });

    if (!string.IsNullOrWhiteSpace(s.TelemetryApiKey))
    {
        if (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var providedKey)
            || providedKey != s.TelemetryApiKey)
        {
            logger.LogWarning("POST /api/telemetry rejected – invalid or missing X-Api-Key from {Remote}",
                ctx.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }
    }

    string body;
    using (var reader = new System.IO.StreamReader(ctx.Request.Body))
        body = await reader.ReadToEndAsync();

    try { System.Text.Json.JsonDocument.Parse(body); }
    catch { return Results.BadRequest("Body is not valid JSON."); }

    if (string.IsNullOrWhiteSpace(s.TelemetryFilePath))
        return Results.BadRequest("TelemetryFilePath is not configured.");

    var dir = Path.GetDirectoryName(s.TelemetryFilePath);
    if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

    await File.WriteAllTextAsync(s.TelemetryFilePath, body, System.Text.Encoding.UTF8);
    logger.LogInformation("Telemetry received via HTTP POST from {Remote} → {Path}",
        ctx.Connection.RemoteIpAddress, s.TelemetryFilePath);

    return Results.Ok(new { written = s.TelemetryFilePath, timestamp = DateTime.UtcNow });
}).DisableAntiforgery();

// Log effective configuration at startup so it is visible in the log file.
// Helpful to verify which settings are actually loaded (appsettings.json vs. env vars).
var startupLog = app.Services.GetRequiredService<ILogger<Program>>();
var cfg        = app.Services.GetRequiredService<IOptions<MeshcomSettings>>().Value;
startupLog.LogInformation(
    "MeshCom effective configuration: " +
    "Device={DeviceIp}:{DevicePort}  Listen={ListenIp}:{ListenPort}  " +
    "Callsign={Callsign}  GroupFilter={GroupFilterEnabled}  Groups=[{Groups}]  " +
    "MonitorMax={MonitorMax}  DataPath={DataPath}  LogPath={LogPath}",
    cfg.DeviceIp, cfg.DevicePort,
    cfg.ListenIp, cfg.ListenPort,
    cfg.MyCallsign,
    cfg.GroupFilterEnabled,
    string.Join(", ", cfg.Groups),
    cfg.MonitorMaxMessages,
    cfg.DataPath,
    cfg.LogPath);

// Open the default browser automatically when MeshcomWebDesk is launched directly
// as an executable (double-click or terminal). Skipped when running in Docker,
// as a Windows service, or under systemd – so no browser pops up on headless systems.
// Also skipped in Development mode (Visual Studio handles it via launchSettings.json).
if (!app.Environment.IsDevelopment())
{
    var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
    lifetime.ApplicationStarted.Register(() =>
    {
        bool isDocker     = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true", StringComparison.OrdinalIgnoreCase);
        bool isWinService = !Environment.UserInteractive && OperatingSystem.IsWindows();
        bool isSystemd    = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("INVOCATION_ID"));

        if (isDocker || isWinService || isSystemd)
        {
            startupLog.LogDebug("Browser auto-open skipped (Docker={Docker}, WinSvc={WinSvc}, Systemd={Systemd})",
                isDocker, isWinService, isSystemd);
            return;
        }

        // Pick the first HTTP address; fall back to whatever is available.
        // Replace 0.0.0.0 (bind-all) with localhost for browser navigation.
        var url = app.Services
            .GetService<IServer>()
            ?.Features.Get<IServerAddressesFeature>()
            ?.Addresses
            .OrderBy(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(url)) return;

        url = url.Replace("0.0.0.0", "localhost", StringComparison.Ordinal)
                 .Replace("[::]",     "localhost", StringComparison.Ordinal);

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            startupLog.LogInformation("Browser opened at {Url}", url);
        }
        catch (Exception ex)
        {
            startupLog.LogWarning(ex, "Could not open browser at {Url}", url);
        }
    });
}

app.Run();
