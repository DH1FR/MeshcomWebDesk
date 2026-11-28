using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace MeshcomWebDesk.Services.Database;

/// <summary>
/// Enforces <see cref="DatabaseSettings.RetentionDays"/>:
/// MySQL – deletes rows older than the limit from the monitor table (in small batches, daily);
/// InfluxDB – sets the retention rule of the bucket (the server then drops old data itself).
/// Only ever touches the configured monitor table / bucket.
/// </summary>
public sealed class DatabaseRetentionService : BackgroundService
{
    /// <summary>Smallest accepted limit – protects against typos wiping nearly everything.</summary>
    public const int MinRetentionDays = 7;

    private const int BatchSize = 5000;
    private static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval   = TimeSpan.FromHours(24);
    private static readonly HttpClient Http     = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<DatabaseRetentionService> _logger;

    public DateTime? LastRunAt     { get; private set; }
    public long      LastDeleted   { get; private set; }
    public string?   LastError     { get; private set; }

    public DatabaseRetentionService(IOptionsMonitor<MeshcomSettings> settings, ILogger<DatabaseRetentionService> logger)
    {
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>Effective limit in days: 0 = unlimited, otherwise at least <see cref="MinRetentionDays"/>.</summary>
    public static int Effective(int days) => days <= 0 ? 0 : Math.Max(days, MinRetentionDays);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunAsync(_settings.CurrentValue.Database, stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger.LogWarning(ex, "Database retention run failed"); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Runs one retention pass for the given settings. Returns the number of deleted rows (MySQL) or 0.</summary>
    public async Task<long> RunAsync(DatabaseSettings db, CancellationToken ct = default)
    {
        LastRunAt = DateTime.UtcNow;
        LastError = null;
        LastDeleted = 0;
        try
        {
            switch (db.Provider)
            {
                case DatabaseSettings.ProviderMySql when Effective(db.RetentionDays) > 0:
                    LastDeleted = await PurgeMySqlAsync(db, ct);
                    break;
                case DatabaseSettings.ProviderInfluxDb when Effective(db.RetentionDays) > 0:   // never touch an admin-set rule when unlimited
                    await ApplyInfluxRetentionAsync(db, ct);
                    break;
            }
            return LastDeleted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            throw;
        }
    }

    // ── MySQL ────────────────────────────────────────────────────────────

    /// <summary>Number of rows older than the configured limit (what a run would delete). −1 = not applicable.</summary>
    public async Task<long> CountExpiredAsync(DatabaseSettings db, CancellationToken ct = default)
    {
        var days = Effective(db.RetentionDays);
        if (db.Provider != DatabaseSettings.ProviderMySql || days == 0) return -1;

        await using var conn = new MySqlConnection(db.MySqlConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            $"SELECT COUNT(*) FROM `{SafeTable(db)}` WHERE timestamp < @cutoff", conn);
        cmd.Parameters.AddWithValue("@cutoff", DateTime.Now.AddDays(-days));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task<long> PurgeMySqlAsync(DatabaseSettings db, CancellationToken ct)
    {
        var cutoff = DateTime.Now.AddDays(-Effective(db.RetentionDays));
        long total = 0;

        await using var conn = new MySqlConnection(db.MySqlConnectionString);
        await conn.OpenAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            await using var cmd = new MySqlCommand(
                $"DELETE FROM `{SafeTable(db)}` WHERE timestamp < @cutoff ORDER BY timestamp LIMIT {BatchSize}", conn);
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            var n = await cmd.ExecuteNonQueryAsync(ct);
            total += n;
            if (n < BatchSize) break;
            await Task.Delay(200, ct);   // give concurrent inserts room
        }

        if (total > 0)
            _logger.LogInformation("Database retention: deleted {Count} entries older than {Days} days from '{Table}'",
                                   total, Effective(db.RetentionDays), db.MySqlTableName);
        return total;
    }

    // The table name comes from the user's own settings; backticks are the only character that could break out.
    private static string SafeTable(DatabaseSettings db) => db.MySqlTableName.Replace("`", "");

    // ── InfluxDB ─────────────────────────────────────────────────────────

    /// <summary>Sets the bucket's retention rule (0 days = keep forever). Only sends a change when it differs.</summary>
    public async Task ApplyInfluxRetentionAsync(DatabaseSettings db, CancellationToken ct = default)
    {
        var baseUrl = db.InfluxUrl.TrimEnd('/');
        var wanted  = Effective(db.RetentionDays) * 86400L;   // seconds, 0 = infinite

        using var getReq = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl}/api/v2/buckets?name={Uri.EscapeDataString(db.InfluxBucket)}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Token", db.InfluxToken);
        using var getResp = await Http.SendAsync(getReq, ct);
        if (!getResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"InfluxDB bucket query failed (HTTP {(int)getResp.StatusCode})");

        using var doc = JsonDocument.Parse(await getResp.Content.ReadAsStringAsync(ct));
        var buckets = doc.RootElement.GetProperty("buckets");
        if (buckets.GetArrayLength() == 0) return;   // bucket not created yet

        var bucket  = buckets[0];
        var id      = bucket.GetProperty("id").GetString();
        long current = 0;
        if (bucket.TryGetProperty("retentionRules", out var rules) && rules.GetArrayLength() > 0 &&
            rules[0].TryGetProperty("everySeconds", out var es))
            current = es.GetInt64();

        if (current == wanted) return;

        var body = JsonSerializer.Serialize(new
        {
            retentionRules = wanted == 0
                ? Array.Empty<object>()
                : new object[] { new { type = "expire", everySeconds = wanted } }
        });
        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"{baseUrl}/api/v2/buckets/{id}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        patch.Headers.Authorization = new AuthenticationHeaderValue("Token", db.InfluxToken);
        using var resp = await Http.SendAsync(patch, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"InfluxDB retention could not be set (HTTP {(int)resp.StatusCode}) – the token needs write access to buckets");

        _logger.LogInformation("InfluxDB: retention of bucket '{Bucket}' set to {Days} days (0 = unlimited)",
                               db.InfluxBucket, Effective(db.RetentionDays));
    }
}
