using MeshcomWebDesk.Components;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services;
using MeshcomWebDesk.Services.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Serilog;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

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
Directory.CreateDirectory(dataPath);
var overrideFile = Path.Combine(dataPath, "appsettings.override.json");
builder.Configuration.AddJsonFile(overrideFile, optional: true, reloadOnChange: true);

Directory.CreateDirectory(logPath);

var logFile = Path.Combine(logPath, "MeshcomWebDesk-.log");

// Enable Static Web Assets when running directly from the build output (not published).
// Without this, a warning is logged and CSS/JS fingerprinting may not resolve correctly.
// For published binaries (GitHub Releases, Docker) this call is a no-op.
builder.WebHost.UseStaticWebAssets();

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    // Console: suppress DB-insert Information logs – they appear in the log file only.
    .WriteTo.Logger(lc => lc
        .MinimumLevel.Override("MeshcomWebDesk.Services.Database", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.Console())
    .WriteTo.File(
        logFile,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainDays,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

// Bind MeshCom settings from configuration
builder.Services.Configure<MeshcomSettings>(meshcomSection);

// Persist Data Protection keys to disk so antiforgery tokens survive container restarts.
// Docker: override via DATAPROTECTION_KEYPATH env variable (e.g. /app/keys).
// Direct start: stored in dataPath/keys next to the other application data.
var keyPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYPATH") ?? Path.Combine(dataPath, "keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(keyPath));

// Register services
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<MeshcomUdpService>();
builder.Services.AddSingleton<DataPersistenceService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<LanguageService>();
builder.Services.AddSingleton<MySqlMonitorSink>();
builder.Services.AddSingleton<InfluxDbMonitorSink>();
builder.Services.AddSingleton<IMonitorDataSink, MonitorSinkService>();
builder.Services.AddSingleton<DatabaseSetupService>();
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeshcomUdpService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DataPersistenceService>());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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
