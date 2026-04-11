using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Database;

/// <summary>
/// Tests an existing database connection and, on request, creates the
/// missing database / table / bucket. Used exclusively by the Settings UI.
/// </summary>
public sealed class DatabaseSetupService
{
    private readonly ILogger<DatabaseSetupService> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public DatabaseSetupService(ILogger<DatabaseSetupService> logger) => _logger = logger;

    // ── Test ─────────────────────────────────────────────────────────────

    public Task<DbTestResult> TestAsync(DatabaseSettings settings, CancellationToken ct = default) =>
        settings.Provider switch
        {
            DatabaseSettings.ProviderMySql    => TestMySqlAsync(settings, ct),
            DatabaseSettings.ProviderInfluxDb => TestInfluxAsync(settings, ct),
            _                                  => Task.FromResult(new DbTestResult(true, true, true, null))
        };

    private async Task<DbTestResult> TestMySqlAsync(DatabaseSettings settings, CancellationToken ct)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(settings.MySqlConnectionString);
            var dbName  = builder.Database;

            // Connect to information_schema so the target database need not exist yet
            builder.Database = "information_schema";
            await using var conn = new MySqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);

            await using var dbCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM SCHEMATA WHERE SCHEMA_NAME = @db", conn);
            dbCmd.Parameters.AddWithValue("@db", dbName);
            var dbExists = Convert.ToInt32(await dbCmd.ExecuteScalarAsync(ct)) > 0;

            if (!dbExists)
                return new(true, false, false, null);

            await using var tblCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tbl", conn);
            tblCmd.Parameters.AddWithValue("@db",  dbName);
            tblCmd.Parameters.AddWithValue("@tbl", settings.MySqlTableName);
            var tblExists = Convert.ToInt32(await tblCmd.ExecuteScalarAsync(ct)) > 0;

            return new(true, true, tblExists, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MySQL: Verbindungstest fehlgeschlagen");
            return new(false, false, false, ex.Message);
        }
    }

    private async Task<DbTestResult> TestInfluxAsync(DatabaseSettings settings, CancellationToken ct)
    {
        try
        {
            var baseUrl = settings.InfluxUrl.TrimEnd('/');

            using var healthReq  = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/health");
            using var healthResp = await _http.SendAsync(healthReq, ct);
            if (!healthResp.IsSuccessStatusCode)
                return new(false, false, false, $"Server nicht erreichbar (HTTP {(int)healthResp.StatusCode})");

            using var bucketReq = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/api/v2/buckets?name={Uri.EscapeDataString(settings.InfluxBucket)}");
            bucketReq.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.InfluxToken);
            using var bucketResp = await _http.SendAsync(bucketReq, ct);
            if (!bucketResp.IsSuccessStatusCode)
                return new(true, true, false, $"Bucket-Abfrage fehlgeschlagen (HTTP {(int)bucketResp.StatusCode})");

            using var doc      = JsonDocument.Parse(await bucketResp.Content.ReadAsStringAsync(ct));
            var bucketExists   = doc.RootElement.GetProperty("buckets").GetArrayLength() > 0;

            return new(true, true, bucketExists, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InfluxDB: Verbindungstest fehlgeschlagen");
            return new(false, false, false, ex.Message);
        }
    }

    // ── Setup ─────────────────────────────────────────────────────────────

    public Task<string?> SetupAsync(DatabaseSettings settings, DbTestResult testResult, CancellationToken ct = default) =>
        settings.Provider switch
        {
            DatabaseSettings.ProviderMySql    => SetupMySqlAsync(settings, testResult, ct),
            DatabaseSettings.ProviderInfluxDb => SetupInfluxAsync(settings, testResult, ct),
            _                                  => Task.FromResult<string?>(null)
        };

    private async Task<string?> SetupMySqlAsync(DatabaseSettings settings, DbTestResult testResult, CancellationToken ct)
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(settings.MySqlConnectionString);
            var dbName  = builder.Database;
            builder.Database = "information_schema";

            await using var conn = new MySqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);

            if (!testResult.DatabaseExists)
            {
                await using var cmd = new MySqlCommand(
                    $"CREATE DATABASE IF NOT EXISTS `{dbName}` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci",
                    conn);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger.LogInformation("MySQL: Datenbank '{Db}' angelegt", dbName);
            }

            await conn.ChangeDatabaseAsync(dbName, ct);

            if (!testResult.TableOrBucketExists)
            {
                var ddl = $"""
                    CREATE TABLE IF NOT EXISTS `{settings.MySqlTableName}` (
                        id                 BIGINT        NOT NULL AUTO_INCREMENT PRIMARY KEY,
                        timestamp          DATETIME(3)   NOT NULL,
                        from_call          VARCHAR(32)   NOT NULL,
                        to_call            VARCHAR(64)   NOT NULL,
                        text               TEXT,
                        rssi               SMALLINT,
                        snr                FLOAT,
                        latitude           DOUBLE,
                        longitude          DOUBLE,
                        altitude           INT,
                        relay_path         VARCHAR(255),
                        msg_id             VARCHAR(16),
                        src_type           VARCHAR(8),
                        battery            TINYINT UNSIGNED,
                        firmware           VARCHAR(16),
                        hw_id              TINYINT UNSIGNED,
                        temp1              FLOAT,
                        temp2              FLOAT,
                        humidity           FLOAT,
                        pressure           FLOAT,
                        is_outgoing        TINYINT(1)    NOT NULL DEFAULT 0,
                        is_position_beacon TINYINT(1)    NOT NULL DEFAULT 0,
                        is_telemetry       TINYINT(1)    NOT NULL DEFAULT 0,
                        INDEX idx_timestamp (timestamp),
                        INDEX idx_from_call (from_call)
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
                    """;
                await using var cmd = new MySqlCommand(ddl, conn);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger.LogInformation("MySQL: Tabelle '{Table}' angelegt", settings.MySqlTableName);
            }
            else
            {
                // Silently add columns that may be missing in tables created by older versions.
                await MigrateTableAsync(conn, settings.MySqlTableName, ct);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MySQL: Setup fehlgeschlagen");
            return ex.Message;
        }
    }

    /// <summary>
    /// Silently adds columns that exist in newer schema versions but may be missing
    /// from tables created by older versions of MeshCom WebDesk.
    /// Uses ALTER TABLE … ADD COLUMN IF NOT EXISTS (MySQL 8.0+).
    /// </summary>
    private async Task MigrateTableAsync(MySqlConnection conn, string tableName, CancellationToken ct)
    {
        var newColumns = new[]
        {
            ("hw_id",    "TINYINT UNSIGNED"),
            ("temp1",    "FLOAT"),
            ("temp2",    "FLOAT"),
            ("humidity", "FLOAT"),
            ("pressure", "FLOAT"),
        };

        foreach (var (col, colDef) in newColumns)
        {
            try
            {
                await using var cmd = new MySqlCommand(
                    $"ALTER TABLE `{tableName}` ADD COLUMN IF NOT EXISTS `{col}` {colDef}", conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("MySQL migration: could not add column '{Col}': {Message}", col, ex.Message);
            }
        }
    }

    private async Task<string?> SetupInfluxAsync(DatabaseSettings settings, DbTestResult testResult, CancellationToken ct)
    {
        if (testResult.TableOrBucketExists)
            return null;

        try
        {
            var baseUrl = settings.InfluxUrl.TrimEnd('/');

            using var orgReq = new HttpRequestMessage(HttpMethod.Get,
                $"{baseUrl}/api/v2/orgs?org={Uri.EscapeDataString(settings.InfluxOrg)}");
            orgReq.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.InfluxToken);
            using var orgResp = await _http.SendAsync(orgReq, ct);
            if (!orgResp.IsSuccessStatusCode)
                return $"Organisation nicht gefunden (HTTP {(int)orgResp.StatusCode})";

            using var orgDoc = JsonDocument.Parse(await orgResp.Content.ReadAsStringAsync(ct));
            var orgs = orgDoc.RootElement.GetProperty("orgs");
            if (orgs.GetArrayLength() == 0)
                return $"Organisation '{settings.InfluxOrg}' nicht gefunden.";

            var orgId = orgs[0].GetProperty("id").GetString();
            var body  = System.Text.Json.JsonSerializer.Serialize(new
            {
                orgID          = orgId,
                name           = settings.InfluxBucket,
                retentionRules = Array.Empty<object>()
            });

            using var bucketReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v2/buckets");
            bucketReq.Headers.Authorization = new AuthenticationHeaderValue("Token", settings.InfluxToken);
            bucketReq.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var bucketResp = await _http.SendAsync(bucketReq, ct);

            if (!bucketResp.IsSuccessStatusCode)
                return $"Bucket konnte nicht angelegt werden: {await bucketResp.Content.ReadAsStringAsync(ct)}";

            _logger.LogInformation("InfluxDB: Bucket '{Bucket}' angelegt", settings.InfluxBucket);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InfluxDB: Setup fehlgeschlagen");
            return ex.Message;
        }
    }
}

public sealed record DbTestResult(
    bool    IsConnected,
    bool    DatabaseExists,
    bool    TableOrBucketExists,
    string? ErrorMessage);
