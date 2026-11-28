using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services.History;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.Database;

/// <summary>
/// Singleton IMonitorDataSink implementation that routes writes to the
/// currently configured backend (MySQL, InfluxDB 2, or nowhere).
/// Provider changes in settings take effect immediately without restart.
/// </summary>
public sealed class MonitorSinkService : IMonitorDataSink
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly MySqlMonitorSink                 _mysql;
    private readonly InfluxDbMonitorSink              _influx;
    private readonly LocalTelemetryHistory            _history;

    // -------------------------------------------------------------------------
    // Status (readable by UI)
    // -------------------------------------------------------------------------

    public bool      IsEnabled        => _settings.CurrentValue.Database.Provider != DatabaseSettings.ProviderNone;
    public bool?     LastWriteOk      { get; private set; }
    public DateTime? LastWriteAt      { get; private set; }

    public MonitorSinkService(
        IOptionsMonitor<MeshcomSettings> settings,
        MySqlMonitorSink                 mysql,
        InfluxDbMonitorSink              influx,
        LocalTelemetryHistory            history)
    {
        _history  = history;
        _settings = settings;
        _mysql    = mysql;
        _influx   = influx;
    }

    public async Task WriteAsync(MeshcomMessage message, CancellationToken ct = default)
    {
        // built-in history (works without a database); must never break the DB write
        try { _history.Record(message); } catch { /* ignore */ }

        try
        {
            await (_settings.CurrentValue.Database.Provider switch
            {
                DatabaseSettings.ProviderMySql    => _mysql.WriteAsync(message, ct),
                DatabaseSettings.ProviderInfluxDb => _influx.WriteAsync(message, ct),
                _                                  => Task.CompletedTask
            });
            LastWriteOk = true;
            LastWriteAt = DateTime.UtcNow;
        }
        catch
        {
            LastWriteOk = false;
            LastWriteAt = DateTime.UtcNow;
            throw;
        }
    }
}
