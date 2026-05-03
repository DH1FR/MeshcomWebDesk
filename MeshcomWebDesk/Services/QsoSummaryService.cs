using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MySqlConnector;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Manages QSO conversation summaries generated via OpenAI API.
/// Summaries are stored in the MySQL table <c>qso_summaries</c> (auto-created).
/// Only active when the database provider is MySQL and AI is enabled.
/// </summary>
public sealed class QsoSummaryService
{
    private const string SummaryTable = "qso_summaries";

    private readonly IOptionsMonitor<MeshcomSettings> _settings;
    private readonly ILogger<QsoSummaryService>        _logger;
    private readonly QrzService                        _qrz;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    // Schema migration: runs once per connection string
    private string _lastMigratedConnStr = string.Empty;
    private readonly SemaphoreSlim _migrateLock = new(1, 1);

    // Token usage tracking (in-memory, resets on restart)
    private long _promptTokens;
    private long _completionTokens;
    private long _requestCount;
    private DateTime? _lastRequestAt;

    public QsoSummaryService(IOptionsMonitor<MeshcomSettings> settings,
                             ILogger<QsoSummaryService> logger,
                             QrzService qrz)
    {
        _settings = settings;
        _logger   = logger;
        _qrz      = qrz;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Returns true when MySQL is configured, regardless of AI settings.</summary>
    public bool IsDbReady
    {
        get
        {
            var db = _settings.CurrentValue.Database;
            return db.Provider == DatabaseSettings.ProviderMySql
                && !string.IsNullOrWhiteSpace(db.MySqlConnectionString);
        }
    }

    /// <summary>Returns the current in-memory token usage statistics.</summary>
    public AiUsageStats GetUsageStats() => new(
        PromptTokens:     _promptTokens,
        CompletionTokens: _completionTokens,
        TotalTokens:      _promptTokens + _completionTokens,
        RequestCount:     _requestCount,
        LastRequestAt:    _lastRequestAt);

    /// <summary>
    /// Tries to retrieve billing info (limit + current month usage) from the OpenAI billing API.
    /// Returns null for non-OpenAI providers or if the request fails.
    /// Returns an error string prefixed with "❌" if the API key is invalid or the endpoint is not accessible.
    /// </summary>
    public async Task<string?> CheckBalanceAsync(CancellationToken ct = default)
    {
        var s = _settings.CurrentValue.Ai;
        if (s.Provider != AiSettings.ProviderOpenAi || string.IsNullOrWhiteSpace(s.ApiKey))
            return null;

        try
        {
            var now       = DateTime.UtcNow;
            var startDate = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
            var endDate   = now.AddDays(1).ToString("yyyy-MM-dd");

            // ── 1. Monthly usage (works with both User-Keys and Project-Keys) ──
            double? usedUsd = null;
            using var usageReq = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.openai.com/v1/dashboard/billing/usage?start_date={startDate}&end_date={endDate}");
            usageReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);
            using var usageResp = await _http.SendAsync(usageReq, ct);
            if (usageResp.IsSuccessStatusCode)
            {
                var usageBody = await usageResp.Content.ReadAsStringAsync(ct);
                using var usageDoc = JsonDocument.Parse(usageBody);
                if (usageDoc.RootElement.TryGetProperty("total_usage", out var tu))
                    usedUsd = tu.GetDouble() / 100.0;   // cents → USD
            }
            else
            {
                var usageBody = await usageResp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("QsoSummaryService: CheckBalanceAsync – usage returned {Status}: {Body}",
                    usageResp.StatusCode, usageBody);

                // OpenAI restricts legacy billing endpoints for project-keys and newer accounts.
                // Return a clear hint with the direct dashboard URL instead of a generic error.
                if (usageResp.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    usageResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return "⚠️ BILLING_API_RESTRICTED";
                }

                try
                {
                    using var errDoc = JsonDocument.Parse(usageBody);
                    if (errDoc.RootElement.TryGetProperty("error", out var err) &&
                        err.TryGetProperty("message", out var msg))
                        return $"❌ {msg.GetString()}";
                }
                catch { /* ignore */ }
                return $"❌ HTTP {(int)usageResp.StatusCode}";
            }

            // ── 2. Subscription / hard limit (User-Keys only, optional) ──────
            double? hardLimit = null;
            bool    hasPm     = false;
            string? planName  = null;

            using var subReq = new HttpRequestMessage(HttpMethod.Get,
                "https://api.openai.com/v1/dashboard/billing/subscription");
            subReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);
            using var subResp = await _http.SendAsync(subReq, ct);
            if (subResp.IsSuccessStatusCode)
            {
                var subBody = await subResp.Content.ReadAsStringAsync(ct);
                using var subDoc = JsonDocument.Parse(subBody);
                var root = subDoc.RootElement;
                hardLimit = root.TryGetProperty("hard_limit_usd", out var hl) ? hl.GetDouble() : null;
                hasPm     = root.TryGetProperty("has_payment_method", out var pm) && pm.GetBoolean();
                planName  = root.TryGetProperty("plan", out var plan) && plan.TryGetProperty("title", out var pt)
                                ? pt.GetString() : null;
            }
            // 403/401 on subscription = Project-Key → silently skip, usage data is enough

            // ── 3. Build result string ────────────────────────────────────────
            var eurRate = await FetchUsdToEurRateAsync(ct);
            var parts   = new List<string>();

            if (planName != null) parts.Add(planName);

            if (hardLimit.HasValue && usedUsd.HasValue)
            {
                var remaining = hardLimit.Value - usedUsd.Value;
                parts.Add(eurRate.HasValue
                    ? $"Restguthaben: ${remaining:F2} / {remaining * eurRate.Value:F2} €"
                    : $"Restguthaben: ${remaining:F2}");
                parts.Add($"{now:MMMM}: ${usedUsd.Value:F2} verbraucht | Limit: ${hardLimit.Value:F2}");
            }
            else if (usedUsd.HasValue)
            {
                parts.Add(eurRate.HasValue
                    ? $"{now:MMMM}: ${usedUsd.Value:F2} / {usedUsd.Value * eurRate.Value:F2} € verbraucht"
                    : $"{now:MMMM}: ${usedUsd.Value:F2} verbraucht");
            }

            if (hasPm) parts.Add("💳");

            return parts.Count > 0 ? string.Join(" | ", parts) : "OK";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: CheckBalanceAsync failed");
            return $"❌ {ex.Message}";
        }
    }


    /// <summary>
    /// Fetches the current USD→EUR exchange rate from the free Frankfurter API.
    /// Returns null on failure (no network, API down, etc.).
    /// </summary>
    private static async Task<double?> FetchUsdToEurRateAsync(CancellationToken ct)
    {
        try
        {
            using var req  = new HttpRequestMessage(HttpMethod.Get, "https://api.frankfurter.app/latest?from=USD&to=EUR");
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc  = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("rates", out var rates) &&
                rates.TryGetProperty("EUR", out var eur))
                return eur.GetDouble();
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns true when either:
    /// <list type="bullet">
    ///   <item>a stored summary already exists for <paramref name="callsignBase"/>, OR</item>
    ///   <item>the monitor table contains direct messages with this station whose last QSO
    ///         is older than <see cref="AiSettings.ThresholdDays"/> (existing data).</item>
    /// </list>
    /// </summary>
    public async Task<bool> HasSummaryAsync(string callsignBase, CancellationToken ct = default)
    {
        if (!IsDbAvailable(out var db, out var ai))
        {
            var s = _settings.CurrentValue;
            _logger.LogInformation("QsoSummaryService: HasSummaryAsync({Callsign}) → IsDbAvailable=false " +
                "(AiEnabled={Enabled}, Provider={Provider}, ConnStr={HasConn})",
                callsignBase, s.Ai.Enabled, s.Database.Provider,
                !string.IsNullOrWhiteSpace(s.Database.MySqlConnectionString));
            return false;
        }

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);
            await EnsureSchemaAsync(conn, db, ct);

            // 1. A stored summary already exists → always show the icon
            await using var summaryCmd = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{SummaryTable}` WHERE callsign_base = @cs", conn);
            summaryCmd.Parameters.AddWithValue("@cs", callsignBase);
            var summaryCount = Convert.ToInt32(await summaryCmd.ExecuteScalarAsync(ct));
            if (summaryCount > 0)
            {
                _logger.LogInformation("QsoSummaryService: HasSummaryAsync({Callsign}) → true (existing summary)", callsignBase);
                return true;
            }

            // 2. No summary yet – check if there are messages in the monitor table
            await using var msgCmd = new MySqlCommand(
                $"""
                SELECT MAX(timestamp) FROM `{db.MySqlTableName}`
                WHERE (from_call = @cs OR from_call LIKE @csLike
                    OR to_call   = @cs OR to_call   LIKE @csLike)
                  AND is_position_beacon = 0
                  AND is_telemetry       = 0
                  AND text IS NOT NULL AND text != ''
                """, conn);
            msgCmd.Parameters.AddWithValue("@cs",     callsignBase);
            msgCmd.Parameters.AddWithValue("@csLike", callsignBase + "-%");

            var result = await msgCmd.ExecuteScalarAsync(ct);
            if (result is null or DBNull)
            {
                _logger.LogInformation("QsoSummaryService: HasSummaryAsync({Callsign}) → false (no messages found in table '{Table}')",
                    callsignBase, db.MySqlTableName);
                return false;
            }

            var lastQso  = Convert.ToDateTime(result);
            var ageDays  = (DateTime.UtcNow - lastQso.ToUniversalTime()).TotalDays;
            var hasIcon  = ageDays >= ai.ThresholdDays;
            _logger.LogInformation(
                "QsoSummaryService: HasSummaryAsync({Callsign}) → {Result} (lastQso={LastQso:yyyy-MM-dd}, age={Age:F1}d, threshold={Threshold}d)",
                callsignBase, hasIcon, lastQso, ageDays, ai.ThresholdDays);
            return hasIcon;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: HasSummaryAsync failed for {Callsign}", callsignBase);
            return false;
        }
    }

    /// <summary>
    /// Reads the stored summary record for <paramref name="callsignBase"/>.
    /// If no summary exists yet but messages are present in the monitor table,
    /// returns a placeholder record so the modal can still show last-QSO info.
    /// </summary>
    public async Task<QsoSummaryRecord?> GetSummaryAsync(string callsignBase, CancellationToken ct = default)
    {
        if (!IsDbAvailable(out var db, out _)) return null;

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);
            await EnsureSchemaAsync(conn, db, ct);

            // Try to load existing summary first
            await using var cmd = new MySqlCommand(
                $"""
                SELECT callsign_base, summary_text, last_qso_at, message_count, created_at
                FROM `{SummaryTable}`
                WHERE callsign_base = @cs
                LIMIT 1
                """, conn);
            cmd.Parameters.AddWithValue("@cs", callsignBase);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                return new QsoSummaryRecord(
                    CallsignBase : reader.GetString(0),
                    SummaryText  : reader.GetString(1),
                    LastQsoAt    : reader.GetDateTime(2),
                    MessageCount : reader.GetInt32(3),
                    CreatedAt    : reader.GetDateTime(4)
                );
            }
            await reader.CloseAsync();

            // No stored summary yet – build a placeholder from existing monitor data
            var lastQso = await GetLastQsoTimeInternalAsync(conn, db, callsignBase, ct);
            if (lastQso is null) return null;

            var msgCount = await GetMessageCountInternalAsync(conn, db, callsignBase, ct);
            return new QsoSummaryRecord(
                CallsignBase : callsignBase,
                SummaryText  : string.Empty,   // empty = "not yet generated" indicator for the modal
                LastQsoAt    : lastQso.Value,
                MessageCount : msgCount,
                CreatedAt    : DateTime.MinValue
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: GetSummaryAsync failed for {Callsign}", callsignBase);
            return null;
        }
    }

    /// <summary>
    /// Reads messages for <paramref name="callsignBase"/> from the monitor table,
    /// sends them to the OpenAI API and stores the result.
    /// </summary>
    public async Task<QsoSummaryRecord?> GenerateSummaryAsync(string callsignBase, string myCallsign, CancellationToken ct = default)
    {
        if (!IsAvailable(out var db, out var ai)) return null;

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);
            await EnsureSchemaAsync(conn, db, ct);

            // Load only direct 1:1 messages between myCallsign and callsignBase
            var messages = await LoadMessagesAsync(conn, db, callsignBase, myCallsign, ai, ct);
            if (messages.Count == 0)
            {
                _logger.LogInformation("QsoSummaryService: no messages found for {Callsign} (SummaryDays={Days})",
                    callsignBase, ai.SummaryDays);
                return null;
            }

            var lastQsoAt    = messages.Max(m => m.Timestamp);
            var messageCount = messages.Count;

            // Build header: only callsigns belonging to the remote station (exact base or with SSID)
            var callsigns = messages
                .SelectMany(m => new[] { m.From, m.To })
                .Where(c => !string.IsNullOrWhiteSpace(c)
                    && (c.Equals(callsignBase, StringComparison.OrdinalIgnoreCase)
                        || c.StartsWith(callsignBase + "-", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
            var callsignHeader = $"📻 {string.Join("  ·  ", callsigns)}\n\n";

            var summaryText = await CallOpenAiAsync(ai, callsignBase, messages, ct);
            if (summaryText is null) return null;

            summaryText = callsignHeader + summaryText;

            // Upsert summary
            await using var upsert = new MySqlCommand(
                $"""
                INSERT INTO `{SummaryTable}` (callsign_base, summary_text, last_qso_at, message_count, created_at)
                VALUES (@cs, @txt, @lqa, @cnt, @now)
                ON DUPLICATE KEY UPDATE
                    summary_text  = VALUES(summary_text),
                    last_qso_at   = VALUES(last_qso_at),
                    message_count = VALUES(message_count),
                    created_at    = VALUES(created_at)
                """, conn);
            upsert.Parameters.AddWithValue("@cs",  callsignBase);
            upsert.Parameters.AddWithValue("@txt", summaryText);
            upsert.Parameters.AddWithValue("@lqa", lastQsoAt);
            upsert.Parameters.AddWithValue("@cnt", messageCount);
            upsert.Parameters.AddWithValue("@now", DateTime.Now);
            await upsert.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("QsoSummaryService: summary generated and stored for {Callsign} ({Count} messages)", callsignBase, messageCount);

            return new QsoSummaryRecord(callsignBase, summaryText, lastQsoAt, messageCount, DateTime.Now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QsoSummaryService: GenerateSummaryAsync failed for {Callsign}", callsignBase);
            return null;
        }
    }

    /// <summary>Returns the timestamp of the last direct message with <paramref name="callsignBase"/> from the DB.</summary>
    public async Task<DateTime?> GetLastQsoTimeAsync(string callsignBase, CancellationToken ct = default)
    {
        if (!IsAvailable(out var db, out _)) return null;

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);

            // Match callsign with or without SSID
            await using var cmd = new MySqlCommand(
                $"""
                SELECT MAX(timestamp) FROM `{db.MySqlTableName}`
                WHERE (from_call = @cs OR from_call LIKE @csLike
                    OR to_call  = @cs OR to_call  LIKE @csLike)
                  AND is_position_beacon = 0
                  AND is_telemetry       = 0
                """, conn);
            cmd.Parameters.AddWithValue("@cs",     callsignBase);
            cmd.Parameters.AddWithValue("@csLike", callsignBase + "-%");

            var result = await cmd.ExecuteScalarAsync(ct);
            return result is null or DBNull ? null : Convert.ToDateTime(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: GetLastQsoTimeAsync failed for {Callsign}", callsignBase);
            return null;
        }
    }

    /// <summary>
    /// Returns the last <paramref name="limit"/> distinct direct QSO partners for
    /// <paramref name="myCallsign"/>, ordered by most recent contact first.
    /// Only direct 1:1 chat messages are considered (no groups, no broadcast, no ACKs).
    /// Returns an empty list when the DB is not available.
    /// </summary>
    public async Task<List<RecentPartner>> GetRecentPartnersAsync(
        string myCallsign, int limit = 20, CancellationToken ct = default)
    {
        // Only MySQL required – no AI key needed
        var db = _settings.CurrentValue.Database;
        if (db.Provider != DatabaseSettings.ProviderMySql
            || string.IsNullOrWhiteSpace(db.MySqlConnectionString)) return [];

        var myBase = myCallsign.Contains('-')
            ? myCallsign[..myCallsign.IndexOf('-')]
            : myCallsign;

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new MySqlCommand(
                $"""
                SELECT
                    CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END AS partner,
                    MAX(timestamp) AS last_contact
                FROM `{db.MySqlTableName}`
                WHERE is_position_beacon = 0
                  AND is_telemetry       = 0
                  AND text IS NOT NULL AND text != ''
                  AND text NOT LIKE '%:ack%'
                  AND (
                      from_call = @my OR from_call LIKE @myLike
                   OR to_call   = @my OR to_call   LIKE @myLike
                  )
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) NOT LIKE '#%'
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) != '*'
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) NOT REGEXP '^[0-9]+$'
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) != ''
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) IS NOT NULL
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) != @my
                  AND (CASE WHEN is_outgoing = 1 THEN to_call ELSE from_call END) NOT LIKE @myLike
                GROUP BY partner
                ORDER BY last_contact DESC
                LIMIT @limit
                """, conn);
            cmd.Parameters.AddWithValue("@my",     myCallsign);
            cmd.Parameters.AddWithValue("@myLike", myBase + "-%");
            cmd.Parameters.AddWithValue("@limit",  limit);

            var list = new List<RecentPartner>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add(new RecentPartner(
                    reader.GetString(0),
                    reader.GetDateTime(1)));
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: GetRecentPartnersAsync failed for {Callsign}", myCallsign);
            return [];
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Loads paginated message history for <paramref name="callsignBase"/> from the monitor table.
    /// Only direct messages between MyCallsign and the remote callsign are returned –
    /// no group messages, no broadcast, no ACKs, no messages to/from third parties.
    /// </summary>
    public async Task<(List<QsoHistoryMessage> Messages, int TotalCount)> GetHistoryAsync(
        string callsignBase,
        string myCallsign,
        DateTime? from = null,
        DateTime? to   = null,
        string?   textFilter = null,
        int       page       = 1,
        int       pageSize   = 50,
        CancellationToken ct = default)
    {
        // Note: ORDER BY ASC → oldest message first (chronological)
        if (!IsDbOnlyAvailable(out var db))
            return ([], 0);

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);

            var where = BuildDirectConversationWhere(callsignBase, myCallsign, from, to, textFilter);

            // Total count
            await using var countCmd = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{db.MySqlTableName}` {where.Sql}", conn);
            foreach (var p in where.Params)
                countCmd.Parameters.AddWithValue(p.Key, p.Value);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

            // Page
            var offset = (page - 1) * pageSize;
            await using var cmd = new MySqlCommand(
                $"""
                SELECT timestamp, from_call, to_call, text, is_outgoing
                FROM `{db.MySqlTableName}`
                {where.Sql}
                ORDER BY timestamp ASC
                LIMIT @limit OFFSET @offset
                """, conn);
            foreach (var p in where.Params)
                cmd.Parameters.AddWithValue(p.Key, p.Value);
            cmd.Parameters.AddWithValue("@limit",  pageSize);
            cmd.Parameters.AddWithValue("@offset", offset);

            var list = new List<QsoHistoryMessage>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add(new QsoHistoryMessage(
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4)));

            return (list, total);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: GetHistoryAsync failed for {Callsign}", callsignBase);
            return ([], 0);
        }
    }

    /// <summary>
    /// Simple full-text search without AI – returns matching messages from the direct
    /// conversation between MyCallsign and the remote callsign.
    /// </summary>
    public async Task<(List<QsoHistoryMessage> Messages, int TotalCount)> TextSearchAsync(
        string callsignBase,
        string myCallsign,
        string searchTerm,
        DateTime? from = null,
        DateTime? to   = null,
        int       page     = 1,
        int       pageSize = 50,
        CancellationToken ct = default)
    {
        if (!IsDbOnlyAvailable(out var db))
            return ([], 0);

        if (string.IsNullOrWhiteSpace(searchTerm))
            return ([], 0);

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);

            var where = BuildDirectConversationWhere(callsignBase, myCallsign, from, to, searchTerm);

            await using var countCmd = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{db.MySqlTableName}` {where.Sql}", conn);
            foreach (var p in where.Params)
                countCmd.Parameters.AddWithValue(p.Key, p.Value);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

            var offset = (page - 1) * pageSize;
            await using var cmd = new MySqlCommand(
                $"""
                SELECT timestamp, from_call, to_call, text, is_outgoing
                FROM `{db.MySqlTableName}`
                {where.Sql}
                ORDER BY timestamp ASC
                LIMIT @limit OFFSET @offset
                """, conn);
            foreach (var p in where.Params)
                cmd.Parameters.AddWithValue(p.Key, p.Value);
            cmd.Parameters.AddWithValue("@limit",  pageSize);
            cmd.Parameters.AddWithValue("@offset", offset);

            var list = new List<QsoHistoryMessage>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add(new QsoHistoryMessage(
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4)));

            return (list, total);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: TextSearchAsync failed for {Callsign}", callsignBase);
            return ([], 0);
        }
    }

    /// <summary>
    /// Sends a natural-language search query to the AI together with relevant messages
    /// and returns the AI answer with found passages and timestamps.
    /// When <paramref name="allDirectContacts"/> is <c>true</c>, all direct 1:1 QSOs
    /// (regardless of remote callsign) are included instead of a single conversation.
    /// </summary>
    public async Task<string?> SearchAsync(
        string callsignBase,
        string myCallsign,
        string query,
        IReadOnlyList<HeardStation>? mhList = null,
        DateTime? from = null,
        DateTime? to   = null,
        bool allDirectContacts = false,
        CancellationToken ct = default)
    {
        if (!IsAvailable(out var db, out var ai))
        {
            var s = _settings.CurrentValue;
            _logger.LogWarning("QsoSummaryService: SearchAsync({Callsign}) skipped – IsAvailable=false " +
                "(AiEnabled={E}, HasKey={K}, Provider={P}, HasConn={C})",
                callsignBase, s.Ai.Enabled, !string.IsNullOrWhiteSpace(s.Ai.ApiKey),
                s.Database.Provider, !string.IsNullOrWhiteSpace(s.Database.MySqlConnectionString));
            return null;
        }

        try
        {
            await using var conn = new MySqlConnection(db.MySqlConnectionString);
            await conn.OpenAsync(ct);

            // Load messages – either all direct QSOs or a single conversation
            var where = allDirectContacts
                ? BuildAllDirectWhere(myCallsign, from, to)
                : BuildDirectConversationWhere(callsignBase, myCallsign, from, to, null);

            await using var cmd = new MySqlCommand(
                $"""
                SELECT timestamp, from_call, to_call, text, is_outgoing
                FROM `{db.MySqlTableName}`
                {where.Sql}
                ORDER BY timestamp ASC
                LIMIT {ai.MaxMessages}
                """, conn);
            foreach (var p in where.Params)
                cmd.Parameters.AddWithValue(p.Key, p.Value);

            var messages = new List<RawMessage>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                messages.Add(new RawMessage(
                    reader.GetDateTime(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));

            _logger.LogInformation(
                "QsoSummaryService: SearchAsync({Mode}) – {Count} messages loaded, query='{Query}'",
                allDirectContacts ? "ALL" : callsignBase, messages.Count, query);

            // ── Station context from QRZ.com and MH list ──────────────────
            var sbCtx = new StringBuilder();

            if (allDirectContacts)
            {
                // In all-contacts mode: list the distinct remote callsigns found in the messages
                var remoteCallsigns = messages
                    .Select(m => m.IsOutgoing ? m.To : m.From)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c)
                    .ToList();

                sbCtx.AppendLine($"Die Suche umfasst alle Direkt-QSOs von {myCallsign}.");
                if (remoteCallsigns.Count > 0)
                    sbCtx.AppendLine($"Enthaltene Rufzeichen: {string.Join(", ", remoteCallsigns)}");
            }
            else
            {
                // Remote station info
                var remoteQrz = _qrz.GetCached(callsignBase);
                var remoteMh  = mhList?.FirstOrDefault(s =>
                    s.Callsign.Equals(callsignBase, StringComparison.OrdinalIgnoreCase) ||
                    s.Callsign.StartsWith(callsignBase + "-", StringComparison.OrdinalIgnoreCase));

                sbCtx.AppendLine($"Informationen zur Gegenstation {callsignBase}:");
                if (remoteQrz?.FirstName != null)
                    sbCtx.AppendLine($"  Vorname: {remoteQrz.FirstName}");
                if (remoteQrz?.Location != null)
                    sbCtx.AppendLine($"  Standort (QRZ.com): {remoteQrz.Location}");
                if (remoteMh != null)
                {
                    if (remoteMh.Latitude.HasValue && remoteMh.Longitude.HasValue)
                    {
                        var grid = LatLonToMaidenhead(remoteMh.Latitude.Value, remoteMh.Longitude.Value);
                        sbCtx.AppendLine($"  GPS-Position: {remoteMh.Latitude:F4}° N, {remoteMh.Longitude:F4}° E");
                        sbCtx.AppendLine($"  QTH-Kenner (Maidenhead): {grid}");
                    }
                    if (remoteMh.Firmware != null)
                        sbCtx.AppendLine($"  Firmware: {remoteMh.Firmware}");
                    sbCtx.AppendLine($"  Zuletzt gehört: {remoteMh.LastHeard:dd.MM.yyyy HH:mm}");
                }
            }

            // Own station info
            var ownQrz = _qrz.GetCached(myCallsign);
            sbCtx.AppendLine($"\nInformationen zur eigenen Station {myCallsign}:");
            if (ownQrz?.FirstName != null)
                sbCtx.AppendLine($"  Vorname: {ownQrz.FirstName}");
            if (ownQrz?.Location != null)
                sbCtx.AppendLine($"  Standort (QRZ.com): {ownQrz.Location}");

            // Check if we have ANY context to answer the question
            bool hasStationContext = allDirectContacts
                ? messages.Count > 0
                : (_qrz.GetCached(callsignBase) != null ||
                   mhList?.Any(s =>
                       s.Callsign.Equals(callsignBase, StringComparison.OrdinalIgnoreCase) ||
                       s.Callsign.StartsWith(callsignBase + "-", StringComparison.OrdinalIgnoreCase)) == true);

            if (messages.Count == 0 && !hasStationContext)
            {
                _logger.LogWarning("QsoSummaryService: SearchAsync({Mode}) – no messages and no station data found " +
                    "(myCallsign={My})", allDirectContacts ? "ALL" : callsignBase, myCallsign);
                return null;
            }

            // Build conversation with clear I/You perspective
            var sb = new StringBuilder();
            foreach (var m in messages)
            {
                var speaker = m.IsOutgoing
                    ? $"ICH ({m.From})"
                    : $"{m.From}";
                sb.AppendLine($"[{m.Timestamp:yyyy-MM-dd HH:mm}] {speaker} an {m.To}: {m.Text}");
            }

            var conversationSection = messages.Count > 0
                ? $"\nNachrichtenverlauf ({messages.Count} Nachrichten):\n{sb}"
                : "\n(Kein Nachrichtenverlauf vorhanden – beantworte die Frage ausschließlich anhand der Stationsdaten.)";

            var scopeDescription = allDirectContacts
                ? $"allen Direkt-QSOs von {myCallsign}"
                : $"dem QSO-Verlauf zwischen {myCallsign} und {callsignBase}";

            var prompt = $"""
                Du hilfst bei Fragen zu {scopeDescription}.
                Der Benutzer ist {myCallsign}. Zeilen mit 'ICH' sind vom Benutzer gesendete Nachrichten.

                Bekannte Stationsdaten (nutze diese vorrangig für Fragen zu Person, Name, QTH, Locator, Standort):
                {sbCtx}

                Beantworte die folgende Frage anhand der Stationsdaten und/oder des Nachrichtenverlaufs.
                Zitiere bei Fundstellen im Nachrichtenverlauf das genaue Datum, die Uhrzeit und den Originaltext.
                Falls nichts Passendes gefunden wird, sage das klar.
                Antworte in der gleichen Sprache wie die Frage.

                Frage: {query}
                {conversationSection}
                """;

            var requestBody = JsonSerializer.Serialize(new
            {
                model    = ai.Model,
                messages = new[]
                {
                    new { role = "system", content = $"Du bist ein hilfreicher Assistent für Amateurfunk. Der Benutzer ist {myCallsign}. Beantworte Fragen zu Stationen anhand der bereitgestellten Stationsdaten (QRZ.com, GPS, Locator) und dem Nachrichtenverlauf. Nachrichten mit 'ICH' wurden vom Benutzer gesendet." },
                    new { role = "user", content = prompt }
                },
                max_tokens  = 800,
                temperature = 0.3
            });

            if (ai.LogRequests)
                _logger.LogInformation("QsoSummaryService: Search request for {Callsign}: {Query}", callsignBase, query);

            using var request = new HttpRequestMessage(HttpMethod.Post, ai.GetApiUrl());
            if (ai.Provider == AiSettings.ProviderAzureOpenAi)
                request.Headers.Add("api-key", ai.ApiKey);
            else
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ai.ApiKey);
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("QsoSummaryService: Search {Provider} returned {Status}: {Body}",
                    ai.Provider, response.StatusCode, responseBody);
                return null;
            }

            using var doc = JsonDocument.Parse(responseBody);
            AccumulateUsage(doc.RootElement);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QsoSummaryService: SearchAsync failed for {Callsign}", callsignBase);
            return null;
        }
    }

    // ── Maidenhead grid locator helper ────────────────────────────────────

    private static string LatLonToMaidenhead(double lat, double lon)
    {
        lon += 180; lat += 90;
        var field1 = (char)('A' + (int)(lon / 20));
        var field2 = (char)('A' + (int)(lat / 10));
        var sq1    = (char)('0' + (int)(lon % 20 / 2));
        var sq2    = (char)('0' + (int)(lat % 10));
        var sub1   = (char)('A' + (int)(lon % 2 * 12));
        var sub2   = (char)('A' + (int)(lat % 1 * 24));
        return $"{field1}{field2}{sq1}{sq2}{sub1}{sub2}";
    }

    // ── WHERE builder ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds a WHERE clause that returns only direct messages between MyCallsign
    /// and the remote callsign (both directions), excluding ACKs, groups and third-party messages.
    /// MyCallsign SSID is stripped for matching so all own SSIDs are covered.
    /// </summary>
    private static (string Sql, Dictionary<string, object> Params) BuildDirectConversationWhere(
        string callsignBase, string myCallsign, DateTime? from, DateTime? to, string? textFilter)
    {
        // Strip own SSID for flexible matching
        var myBase = myCallsign.Contains('-')
            ? myCallsign[..myCallsign.IndexOf('-')]
            : myCallsign;

        // Direct conversation: (remote→me) OR (me→remote)
        // Excludes messages to/from third parties and ACK-only messages
        // @cs matches the exact base callsign (e.g. DH1FR),
        // @csLike matches only SSIDs (e.g. DH1FR-1, DH1FR-12) – NOT DH1FRX.
        var conditions = new List<string>
        {
            """
            (
                ((from_call = @cs OR from_call LIKE @csLike) AND (to_call = @myCall OR to_call LIKE @myLike))
             OR ((to_call   = @cs OR to_call   LIKE @csLike) AND (from_call = @myCall OR from_call LIKE @myLike))
            )
            """,
            "is_position_beacon = 0",
            "is_telemetry = 0",
            "is_outgoing IS NOT NULL",
            "text IS NOT NULL AND text != ''",
            // Exclude all ACK messages: pattern is "CALLSIGN :ackNNN" or "CALLSIGN-N :ackNNN"
            "text NOT LIKE '%:ack%'"
        };

        var parms = new Dictionary<string, object>
        {
            ["@cs"]     = callsignBase,            // exact base callsign (e.g. DH1FR)
            ["@csLike"] = callsignBase + "-%",     // SSIDs only: DH1FR-1, DH1FR-12 – NOT DH1FRX
            ["@myCall"] = myCallsign,
            ["@myLike"] = myBase + "-%"            // own SSIDs only: DH1FR-1 etc.
        };

        if (from.HasValue) { conditions.Add("timestamp >= @from"); parms["@from"] = from.Value; }
        if (to.HasValue)   { conditions.Add("timestamp <= @to");   parms["@to"]   = to.Value; }
        if (!string.IsNullOrWhiteSpace(textFilter))
        {
            conditions.Add("text LIKE @txt");
            parms["@txt"] = $"%{textFilter}%";
        }

        return ($"WHERE {string.Join(" AND ", conditions)}", parms);
    }

    /// <summary>
    /// Builds a WHERE clause that covers ALL direct 1:1 QSOs involving <paramref name="myCallsign"/>,
    /// regardless of the remote callsign. Group messages (#...) and broadcasts (*) are excluded.
    /// </summary>
    private static (string Sql, Dictionary<string, object> Params) BuildAllDirectWhere(
        string myCallsign, DateTime? from, DateTime? to)
    {
        var myBase = myCallsign.Contains('-')
            ? myCallsign[..myCallsign.IndexOf('-')]
            : myCallsign;

        // Must involve own callsign (any SSID) on either side
        // AND remote side must not be a group (#...) or broadcast (*  / ALL)
        var conditions = new List<string>
        {
            """
            (
                (from_call = @myCall OR from_call LIKE @myLike)
             OR (to_call   = @myCall OR to_call   LIKE @myLike)
            )
            """,
            // Exclude group and broadcast destinations/sources
            "to_call   NOT LIKE '#%'",
            "from_call NOT LIKE '#%'",
            "to_call   NOT IN ('*','ALL','all')",
            "from_call NOT IN ('*','ALL','all')",
            "is_position_beacon = 0",
            "is_telemetry = 0",
            "is_outgoing IS NOT NULL",
            "text IS NOT NULL AND text != ''",
            "text NOT LIKE '%:ack%'"
        };

        var parms = new Dictionary<string, object>
        {
            ["@myCall"] = myCallsign,
            ["@myLike"] = myBase + "-%"
        };

        if (from.HasValue) { conditions.Add("timestamp >= @from"); parms["@from"] = from.Value; }
        if (to.HasValue)   { conditions.Add("timestamp <= @to");   parms["@to"]   = to.Value; }

        return ($"WHERE {string.Join(" AND ", conditions)}", parms);
    }

    /// <summary>
    /// Full availability check: AI enabled + API key set + MySQL configured.
    /// Used before calling the AI API (Generate).
    /// </summary>
    private bool IsAvailable(out DatabaseSettings db, out AiSettings ai)
    {
        var s = _settings.CurrentValue;
        db = s.Database;
        ai = s.Ai;
        return ai.Enabled
            && !string.IsNullOrWhiteSpace(ai.ApiKey)
            && db.Provider == DatabaseSettings.ProviderMySql
            && !string.IsNullOrWhiteSpace(db.MySqlConnectionString);
    }

    /// <summary>
    /// DB-only availability check: AI enabled + MySQL configured (no API key required).
    /// Used for HasSummaryAsync and GetSummaryAsync – the icon should appear even before
    /// the user has entered an API key, as long as data exists in the database.
    /// </summary>
    private bool IsDbAvailable(out DatabaseSettings db, out AiSettings ai)
    {
        var s = _settings.CurrentValue;
        db = s.Database;
        ai = s.Ai;
        return ai.Enabled
            && db.Provider == DatabaseSettings.ProviderMySql
            && !string.IsNullOrWhiteSpace(db.MySqlConnectionString);
    }

    /// <summary>
    /// DB availability check without requiring AI to be enabled.
    /// Used for history and text search which work independently of AI.
    /// </summary>
    private bool IsDbOnlyAvailable(out DatabaseSettings db)
    {
        db = _settings.CurrentValue.Database;
        return db.Provider == DatabaseSettings.ProviderMySql
            && !string.IsNullOrWhiteSpace(db.MySqlConnectionString);
    }

    private static async Task<DateTime?> GetLastQsoTimeInternalAsync(
        MySqlConnection conn, DatabaseSettings db, string callsignBase, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            $"""
            SELECT MAX(timestamp) FROM `{db.MySqlTableName}`
            WHERE (from_call = @cs OR from_call LIKE @csLike
                OR to_call   = @cs OR to_call   LIKE @csLike)
              AND is_position_beacon = 0
              AND is_telemetry       = 0
              AND text IS NOT NULL AND text != ''
            """, conn);
        cmd.Parameters.AddWithValue("@cs",     callsignBase);
        cmd.Parameters.AddWithValue("@csLike", callsignBase + "-%");
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : Convert.ToDateTime(result);
    }

    private static async Task<int> GetMessageCountInternalAsync(
        MySqlConnection conn, DatabaseSettings db, string callsignBase, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            $"""
            SELECT COUNT(*) FROM `{db.MySqlTableName}`
            WHERE (from_call = @cs OR from_call LIKE @csLike
                OR to_call   = @cs OR to_call   LIKE @csLike)
              AND is_position_beacon = 0
              AND is_telemetry       = 0
              AND text IS NOT NULL AND text != ''
            """, conn);
        cmd.Parameters.AddWithValue("@cs",     callsignBase);
        cmd.Parameters.AddWithValue("@csLike", callsignBase + "-%");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task<List<RawMessage>> LoadMessagesAsync(
        MySqlConnection conn, DatabaseSettings db, string callsignBase, string myCallsign, AiSettings ai, CancellationToken ct)
    {
        // Use the same direct-conversation filter as History/Search tabs:
        // only 1:1 messages between myCallsign ↔ callsignBase (no groups, no broadcast, no third parties)
        var from = ai.SummaryDays > 0 ? DateTime.Now.AddDays(-ai.SummaryDays) : (DateTime?)null;
        var where = BuildDirectConversationWhere(callsignBase, myCallsign, from, to: null, textFilter: null);

        await using var cmd = new MySqlCommand(
            $"""
            SELECT timestamp, from_call, to_call, text, is_outgoing
            FROM `{db.MySqlTableName}`
            {where.Sql}
            ORDER BY timestamp DESC
            LIMIT @max
            """, conn);
        foreach (var p in where.Params)
            cmd.Parameters.AddWithValue(p.Key, p.Value);
        cmd.Parameters.AddWithValue("@max", ai.MaxMessages);

        var list = new List<RawMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new RawMessage(
                reader.GetDateTime(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4)));
        }

        // Reverse to chronological order for the prompt
        list.Reverse();
        return list;
    }

    private async Task<string?> CallOpenAiAsync(
        AiSettings ai, string callsignBase, List<RawMessage> messages, CancellationToken ct)
    {
        // Build conversation text for the prompt
        var sb = new StringBuilder();
        foreach (var m in messages)
        {
            var dir = m.IsOutgoing ? "→" : "←";
            sb.AppendLine($"[{m.Timestamp:yyyy-MM-dd HH:mm}] {dir} {m.From} → {m.To}: {m.Text}");
        }

        var prompt = $"""
            The following are MeshCom radio QSO messages exchanged with station {callsignBase}.
            Please provide a concise summary organized by date (newest date first).
            For each day include: date, number of messages, and a brief summary of topics discussed.
            Also mention the date and time of the last QSO.
            Respond in the same language as the messages. If messages are mixed language, use German.

            Messages:
            {sb}
            """;

        var requestBody = JsonSerializer.Serialize(new
        {
            model    = ai.Model,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant summarizing amateur radio QSO conversations." },
                new { role = "user",   content = prompt }
            },
            max_tokens  = 1000,
            temperature = 0.3
        });

        if (ai.LogRequests)
            _logger.LogInformation("QsoSummaryService: {Provider} request for {Callsign}:\n{Body}",
                ai.Provider, callsignBase, requestBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, ai.GetApiUrl());

        // Azure OpenAI uses "api-key" header; OpenAI and Grok use "Authorization: Bearer"
        if (ai.Provider == AiSettings.ProviderAzureOpenAi)
            request.Headers.Add("api-key", ai.ApiKey);
        else
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ai.ApiKey);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("QsoSummaryService: {Provider} returned {Status}: {Body}",
                ai.Provider, response.StatusCode, responseBody);
            return null;
        }

        if (ai.LogRequests)
            _logger.LogInformation("QsoSummaryService: {Provider} response:\n{Body}", ai.Provider, responseBody);

        using var doc = JsonDocument.Parse(responseBody);
        AccumulateUsage(doc.RootElement);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    /// <summary>Creates the <c>qso_summaries</c> table if it does not exist yet.</summary>
    private async Task EnsureSchemaAsync(MySqlConnection conn, DatabaseSettings db, CancellationToken ct)
    {
        if (_lastMigratedConnStr == db.MySqlConnectionString) return;

        await _migrateLock.WaitAsync(ct);
        try
        {
            if (_lastMigratedConnStr == db.MySqlConnectionString) return;

            await using var cmd = new MySqlCommand(
                $"""
                CREATE TABLE IF NOT EXISTS `{SummaryTable}` (
                    id             INT          AUTO_INCREMENT PRIMARY KEY,
                    callsign_base  VARCHAR(20)  NOT NULL,
                    summary_text   TEXT         NOT NULL,
                    last_qso_at    DATETIME     NOT NULL,
                    message_count  INT          NOT NULL,
                    created_at     DATETIME     NOT NULL,
                    UNIQUE KEY uk_callsign (callsign_base)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
                """, conn);
            await cmd.ExecuteNonQueryAsync(ct);

            _lastMigratedConnStr = db.MySqlConnectionString;
            _logger.LogInformation("QsoSummaryService: table '{Table}' ensured", SummaryTable);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QsoSummaryService: EnsureSchemaAsync failed");
        }
        finally
        {
            _migrateLock.Release();
        }
    }

    // ── Internal record types ─────────────────────────────────────────────

    private void AccumulateUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return;
        if (usage.TryGetProperty("prompt_tokens",     out var p)) Interlocked.Add(ref _promptTokens,     p.GetInt64());
        if (usage.TryGetProperty("completion_tokens", out var c)) Interlocked.Add(ref _completionTokens, c.GetInt64());
        Interlocked.Increment(ref _requestCount);
        _lastRequestAt = DateTime.Now;
    }

    private sealed record RawMessage(DateTime Timestamp, string From, string To, string Text, bool IsOutgoing);
}

/// <summary>A stored QSO summary record.</summary>
public sealed record QsoSummaryRecord(
    string   CallsignBase,
    string   SummaryText,
    DateTime LastQsoAt,
    int      MessageCount,
    DateTime CreatedAt
);

/// <summary>A single message from the QSO history.</summary>
public sealed record QsoHistoryMessage(
    DateTime Timestamp,
    string   From,
    string   To,
    string   Text,
    bool     IsOutgoing
);

/// <summary>In-memory KI/AI token usage statistics (resets on application restart).</summary>
public sealed record AiUsageStats(
    long      PromptTokens,
    long      CompletionTokens,
    long      TotalTokens,
    long      RequestCount,
    DateTime? LastRequestAt
);

/// <summary>A recent direct QSO partner with timestamp of last contact.</summary>
public sealed record RecentPartner(string Callsign, DateTime LastContact);
