using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Decrypts sensitive fields in <see cref="MeshcomSettings"/> after they are loaded
/// from <c>appsettings.override.json</c>.
///
/// Supported prefixes:
/// - <c>"aes:"</c> – new platform-independent AES-256-GCM encryption (written by current SettingsService)
/// - <c>"dp:"</c>  – legacy ASP.NET Core Data Protection (written by older versions); decrypted
///                   transparently on the same platform so existing users are not affected.
/// - no prefix     – plain-text value, passed through unchanged.
/// </summary>
public sealed class DecryptMeshcomSettingsPostConfigure : IPostConfigureOptions<MeshcomSettings>
{
    private readonly ISettingsProtector _aes;
    private readonly IDataProtector _dp;

    public DecryptMeshcomSettingsPostConfigure(ISettingsProtector aes, IDataProtectionProvider dpProvider)
    {
        _aes = aes;
        _dp  = dpProvider.CreateProtector("MeshcomWebDesk.Settings.v1");
    }

    public void PostConfigure(string? name, MeshcomSettings options)
    {
        options.Database.MySqlConnectionString = Decrypt(options.Database.MySqlConnectionString);
        options.Database.InfluxToken           = Decrypt(options.Database.InfluxToken);
        options.Qrz.Password                   = Decrypt(options.Qrz.Password);
        options.TelemetryApiKey                = Decrypt(options.TelemetryApiKey);
        options.Mqtt.Password                  = Decrypt(options.Mqtt.Password);
        options.Ai.ApiKey                      = Decrypt(options.Ai.ApiKey);
        options.TelnetPassword                 = Decrypt(options.TelnetPassword);
        options.WeatherApi.ApiKey              = Decrypt(options.WeatherApi.ApiKey);

        foreach (var node in options.Nodes)
            node.TelnetPassword = Decrypt(node.TelnetPassword);

        // Migration: MhMaxAgeDays (days) → MhMaxAgeHours (hours).
        if (options.MhMaxAgeDays > 0 && options.MhMaxAgeHours == 0)
            options.MhMaxAgeHours = options.MhMaxAgeDays * 24;

        // One-time migration (v1.18.4): whatever gateway source was stored, it becomes "all" once.
        // The marker is persisted by SettingsService on every save, so any choice made
        // afterwards sticks.
        if (!options.GatewayAllDefaultApplied)
        {
            options.GatewayServer = "all";
            options.GatewayAllDefaultApplied = true;
        }
    }

    private string Decrypt(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // New AES-256-GCM format
        if (value.StartsWith("aes:", StringComparison.Ordinal))
            return _aes.TryDecrypt(value);

        // Legacy Data Protection – works on same platform/machine, transparent for all existing users
        if (value.StartsWith("dp:", StringComparison.Ordinal))
        {
            try { return _dp.Unprotect(value[3..]); }
            catch { return string.Empty; } // cross-platform migration: user must re-enter once
        }

        return value; // plain text
    }
}
