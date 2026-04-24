using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Decrypts sensitive fields in <see cref="MeshcomSettings"/> after they are loaded
/// from <c>appsettings.override.json</c>. Values encrypted by <see cref="SettingsService"/>
/// carry a <c>"dp:"</c> prefix; unprotected plain-text values pass through unchanged
/// for backward compatibility and manual edits.
/// </summary>
public sealed class DecryptMeshcomSettingsPostConfigure : IPostConfigureOptions<MeshcomSettings>
{
    private readonly IDataProtector _protector;

    public DecryptMeshcomSettingsPostConfigure(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("MeshcomWebDesk.Settings.v1");
    }

    public void PostConfigure(string? name, MeshcomSettings options)
    {
        options.Database.MySqlConnectionString = TryDecrypt(options.Database.MySqlConnectionString);
        options.Database.InfluxToken           = TryDecrypt(options.Database.InfluxToken);
        options.Qrz.Password                   = TryDecrypt(options.Qrz.Password);
        options.TelemetryApiKey                = TryDecrypt(options.TelemetryApiKey);
        options.Mqtt.Password                  = TryDecrypt(options.Mqtt.Password);
    }

    private string TryDecrypt(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith("dp:", StringComparison.Ordinal))
            return value;
        try   { return _protector.Unprotect(value[3..]); }
        catch { return value; }   // key rotation / migration fallback: return as-is
    }
}
