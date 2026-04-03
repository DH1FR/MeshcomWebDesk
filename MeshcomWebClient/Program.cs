using MeshcomWebClient.Components;
using MeshcomWebClient.Models;
using MeshcomWebClient.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Read Meshcom settings early for log path configuration
var meshcomSection = builder.Configuration.GetSection(MeshcomSettings.SectionName);
var logPath = meshcomSection.GetValue<string>("LogPath") ?? @"C:\Temp\Logs";
var retainDays = meshcomSection.GetValue<int?>("LogRetainDays") ?? 30;

Directory.CreateDirectory(logPath);

var logFile = Path.Combine(logPath, "MeshcomWebClient-.log");

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        logFile,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainDays,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

// Bind MeshCom settings from configuration
builder.Services.Configure<MeshcomSettings>(meshcomSection);

// Persist Data Protection keys to disk so antiforgery tokens survive container restarts.
// The path is configurable via the environment variable DATAPROTECTION_KEYPATH (default: /app/keys).
var keyPath = Environment.GetEnvironmentVariable("DATAPROTECTION_KEYPATH") ?? "/app/keys";
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new System.IO.DirectoryInfo(keyPath));

// Register services
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<MeshcomUdpService>();
builder.Services.AddSingleton<DataPersistenceService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MeshcomUdpService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DataPersistenceService>());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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

app.Run();
