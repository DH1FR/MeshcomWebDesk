namespace MeshcomWebDesk.Models;

public class DatabaseSettings
{
    public const string ProviderNone     = "none";
    public const string ProviderMySql    = "mysql";
    public const string ProviderInfluxDb = "influxdb2";

    /// <summary>Active database provider. "none" disables writing entirely.</summary>
    public string Provider { get; set; } = ProviderNone;

    // ── MySQL / MariaDB ───────────────────────────────────────────────────

    /// <summary>ADO.NET connection string, e.g. "Server=localhost;Database=meshcom;User=mc;Password=secret;"</summary>
    public string MySqlConnectionString { get; set; } = string.Empty;

    /// <summary>Target table. Created automatically via Settings → "Anlegen".</summary>
    public string MySqlTableName { get; set; } = "meshcom_monitor";

    // ── InfluxDB 2 ────────────────────────────────────────────────────────

    public string InfluxUrl    { get; set; } = "http://localhost:8086";
    public string InfluxToken  { get; set; } = string.Empty;
    public string InfluxOrg    { get; set; } = "meshcom";
    public string InfluxBucket { get; set; } = "meshcom";

    // ── Retention ─────────────────────────────────────────────────────────

    /// <summary>How long monitor data is kept: 0 = unlimited, otherwise days (minimum 7).
    /// MySQL: daily purge by WebDesk; InfluxDB: retention rule of the bucket.</summary>
    public int RetentionDays { get; set; } = 0;

    // ── Logging ───────────────────────────────────────────────────────────

    /// <summary>When true, every successful DB write is logged at Information level.</summary>
    public bool LogInserts { get; set; } = false;
}
