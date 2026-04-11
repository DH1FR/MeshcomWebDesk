using MySqlConnector;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services.Database;

public sealed class MySqlMonitorSink
{
    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<MySqlMonitorSink>        _logger;

    // Migration runs once per connection-string; reset when the connection string changes.
    private string _lastMigratedConnStr = string.Empty;
    private readonly SemaphoreSlim _migrateLock = new(1, 1);

    private static readonly (string Column, string Definition)[] _newColumns =
    [
        ("hw_id",    "TINYINT UNSIGNED"),
        ("temp1",    "FLOAT"),
        ("temp2",    "FLOAT"),
        ("humidity", "FLOAT"),
        ("pressure", "FLOAT"),
    ];

    public MySqlMonitorSink(IOptionsMonitor<MeshcomSettings> settings, ILogger<MySqlMonitorSink> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    public async Task WriteAsync(MeshcomMessage msg, CancellationToken ct = default)
    {
        var db = _settings.CurrentValue.Database;
        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);

            await EnsureMigratedAsync(conn, db, ct);

            var sql = $"""
                INSERT INTO `{db.MySqlTableName}`
                    (timestamp, from_call, to_call, text, rssi, snr,
                     latitude, longitude, altitude, relay_path, msg_id,
                     src_type, battery, firmware, hw_id,
                     temp1, temp2, humidity, pressure,
                     is_outgoing, is_position_beacon, is_telemetry)
                VALUES
                    (@ts, @from, @to, @text, @rssi, @snr,
                     @lat, @lon, @alt, @relay, @msgId,
                     @srcType, @batt, @fw, @hwId,
                     @temp1, @temp2, @hum, @press,
                     @out, @pos, @tele)
                """;

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ts",      msg.Timestamp);
            cmd.Parameters.AddWithValue("@from",    msg.From);
            cmd.Parameters.AddWithValue("@to",      msg.To);
            cmd.Parameters.AddWithValue("@text",    msg.Text);
            cmd.Parameters.AddWithValue("@rssi",    (object?)msg.Rssi      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@snr",     (object?)msg.Snr       ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lat",     (object?)msg.Latitude  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lon",     (object?)msg.Longitude ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@alt",     (object?)msg.Altitude  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@relay",   (object?)msg.RelayPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@msgId",   (object?)msg.MsgId     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@srcType", (object?)msg.SrcType   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@batt",    (object?)msg.Battery   ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fw",      (object?)msg.Firmware  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hwId",    (object?)msg.HwId      ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@temp1",   (object?)msg.Temp1     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@temp2",   (object?)msg.Temp2     ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hum",     (object?)msg.Humidity  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@press",   (object?)msg.Pressure  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@out",     msg.IsOutgoing);
            cmd.Parameters.AddWithValue("@pos",     msg.IsPositionBeacon);
            cmd.Parameters.AddWithValue("@tele",    msg.IsTelemetry);

            await cmd.ExecuteNonQueryAsync(ct);

            if (db.LogInserts)
            {
                var logSql = $"""
                    INSERT INTO `{db.MySqlTableName}`
                        (timestamp, from_call, to_call, text, rssi, snr,
                         latitude, longitude, altitude, relay_path, msg_id,
                         src_type, battery, firmware, hw_id,
                         temp1, temp2, humidity, pressure,
                         is_outgoing, is_position_beacon, is_telemetry)
                    VALUES
                        ('{msg.Timestamp:yyyy-MM-dd HH:mm:ss.fff}', '{msg.From}', '{msg.To}', '{msg.Text.Replace("'", "''")}',
                         {N(msg.Rssi)}, {N(msg.Snr)},
                         {N(msg.Latitude)}, {N(msg.Longitude)}, {N(msg.Altitude)},
                         {S(msg.RelayPath)}, {S(msg.MsgId)},
                         {S(msg.SrcType)}, {N(msg.Battery)}, {S(msg.Firmware)}, {N(msg.HwId)},
                         {N(msg.Temp1)}, {N(msg.Temp2)}, {N(msg.Humidity)}, {N(msg.Pressure)},
                         {B(msg.IsOutgoing)}, {B(msg.IsPositionBeacon)}, {B(msg.IsTelemetry)})
                    """;
                _logger.LogInformation("DB INSERT:\n{Sql}", logSql);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL: Fehler beim Schreiben der Monitor-Daten");
        }
    }

    // ── Schema migration ──────────────────────────────────────────────────

    /// <summary>
    /// Adds any missing columns to an existing table (runs once per unique connection string).
    /// Uses ALTER TABLE … ADD COLUMN IF NOT EXISTS – safe on MySQL 8.0+.
    /// </summary>
    private async Task EnsureMigratedAsync(MySqlConnection conn, DatabaseSettings db, CancellationToken ct)
    {
        if (_lastMigratedConnStr == db.MySqlConnectionString) return;

        await _migrateLock.WaitAsync(ct);
        try
        {
            if (_lastMigratedConnStr == db.MySqlConnectionString) return;

            foreach (var (col, def) in _newColumns)
            {
                try
                {
                    await using var cmd = new MySqlCommand(
                        $"ALTER TABLE `{db.MySqlTableName}` ADD COLUMN IF NOT EXISTS `{col}` {def}", conn);
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("MySqlMonitorSink migration: skipping column '{Col}': {Msg}", col, ex.Message);
                }
            }

            _lastMigratedConnStr = db.MySqlConnectionString;
            _logger.LogInformation("MySqlMonitorSink: schema migration complete for table '{Table}'", db.MySqlTableName);
        }
        finally
        {
            _migrateLock.Release();
        }
    }

    // ── Log-Hilfsmethoden (nur für die lesbare SQL-Darstellung) ───────────
    private static string N(object? v)  => v is null ? "NULL" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;
    private static string S(string? v)  => v is null ? "NULL" : $"'{v.Replace("'", "''")}'";
    private static string B(bool v)     => v ? "1" : "0";
}
