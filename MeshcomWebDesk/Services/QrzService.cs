using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Queries the QRZ.com XML API for callsign information (first name and location).
/// Session keys are cached and refreshed automatically on expiry.
/// Callsign results are persisted to disk (qrz-cache.json in DataPath) so that
/// each callsign is only queried once – even across application restarts.
/// </summary>
public class QrzService
{
    private const string BaseUrl      = "https://xmldata.qrz.com/xml/current/";
    private const string CacheFileName = "qrz-cache.json";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true };

    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<QrzService> _logger;
    private readonly HttpClient _http;
    private readonly string _cacheFilePath;
    private readonly object _fileLock = new();

    private string? _sessionKey;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly ConcurrentDictionary<string, QrzCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public QrzService(IOptionsMonitor<MeshcomSettings> settingsMonitor, ILogger<QrzService> logger)
    {
        _settingsMonitor = settingsMonitor;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        var dataPath = settingsMonitor.CurrentValue.DataPath;
        Directory.CreateDirectory(dataPath);
        _cacheFilePath = Path.Combine(dataPath, CacheFileName);
        LoadCacheFromDisk();
    }

    /// <summary>
    /// Returns QRZ info for the given callsign, or null when disabled / not found / on error.
    /// Results are cached permanently per callsign for the lifetime of the application.
    /// </summary>
    public async Task<QrzInfo?> LookupAsync(string callsign)
    {
        var settings = _settingsMonitor.CurrentValue.Qrz;
        if (!settings.Enabled) return null;

        callsign = callsign.ToUpperInvariant();

        // Strip SSID suffix (e.g. "DH1FR-2" → "DH1FR")
        var bare = callsign.Contains('-') ? callsign[..callsign.IndexOf('-')] : callsign;

        if (_cache.TryGetValue(bare, out var cached) && cached is not null)
        {
            var maxAge = settings.CacheMaxAgeDays;
            var ageDays = (DateTime.UtcNow - cached.CachedAt).TotalDays;
            if (maxAge <= 0 || ageDays < maxAge)
            {
                if (settings.LogRequests)
                    _logger.LogInformation("QRZ lookup (cache): {Callsign} → {Result} (age: {Age:F0}d)", bare,
                        cached.Info is null ? "not found" : $"{cached.Info.FirstName}, {cached.Info.Location}",
                        ageDays);
                return cached.Info;
            }
            _cache.TryRemove(bare, out _);
            if (settings.LogRequests)
                _logger.LogInformation("QRZ cache expired for {Callsign} ({Age:F0}d ≥ {Max}d), refreshing…", bare, ageDays, maxAge);
        }

        var result = await FetchAsync(bare, settings);
        if (settings.LogRequests)
            _logger.LogInformation("QRZ lookup (API): {Callsign} → {Result}", bare,
                result is null ? "not found" : $"{result.FirstName}, {result.Location}");
        _cache[bare] = new QrzCacheEntry(result, DateTime.UtcNow);
        SaveCacheToDisk();
        return result;
    }

    /// <summary>Clears the in-memory callsign cache, the session key and the on-disk cache file.</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _sessionKey = null;
        lock (_fileLock)
        {
            try { File.Delete(_cacheFilePath); }
            catch (Exception ex) { _logger.LogWarning("Failed to delete QRZ cache file: {Message}", ex.Message); }
        }
    }

    /// <summary>
    /// Returns cached QRZ data synchronously without making any network request.
    /// Returns null when the callsign has not been looked up yet or has no QRZ entry.
    /// </summary>
    public QrzInfo? GetCached(string callsign)
    {
        var bare = callsign.Contains('-') ? callsign[..callsign.IndexOf('-')] : callsign;
        return _cache.TryGetValue(bare, out var entry) && entry is not null ? entry.Info : null;
    }

    // ── Disk persistence ───────────────────────────────────────────────────────

    private void LoadCacheFromDisk()
    {
        if (!File.Exists(_cacheFilePath)) return;
        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, QrzCacheEntry>>(json);
            if (dict is null) return;
            foreach (var (key, value) in dict)
                if (value is not null)
                    _cache[key] = value;
            _logger.LogInformation("QRZ cache restored: {Count} entries from {Path}", dict.Count, _cacheFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load QRZ cache from disk (format changed?): {Message}", ex.Message);
        }
    }

    private void SaveCacheToDisk()
    {
        lock (_fileLock)
        {
            try
            {
                var snapshot = _cache.ToDictionary(kv => kv.Key, kv => kv.Value);
                var json     = JsonSerializer.Serialize(snapshot, JsonOpts);
                File.WriteAllText(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to save QRZ cache to disk: {Message}", ex.Message);
            }
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<QrzInfo?> FetchAsync(string callsign, QrzSettings settings)
    {
        // Ensure we have a valid session key (retry once on session expiry)
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = await EnsureSessionKeyAsync(settings);
            if (key is null) return null;

            try
            {
                var url = $"{BaseUrl}?s={Uri.EscapeDataString(key)};callsign={Uri.EscapeDataString(callsign)}";
                if (settings.LogRequests)
                    _logger.LogInformation("QRZ >> GET {Url}", $"{BaseUrl}?s=***;callsign={Uri.EscapeDataString(callsign)}");
                var xml = await _http.GetStringAsync(url);
                if (settings.LogRequests)
                    _logger.LogInformation("QRZ << {Response}", xml);
                var doc = XDocument.Parse(xml);
                XNamespace ns = "http://xmldata.qrz.com";

                // Check for session error (expired key)
                var sessionError = doc.Descendants(ns + "Error").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(sessionError))
                {
                    if (sessionError.Contains("Session Timeout", StringComparison.OrdinalIgnoreCase) ||
                        sessionError.Contains("Invalid session", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("QRZ session expired, refreshing…");
                        _sessionKey = null;
                        continue; // retry with new session
                    }

                    if (sessionError.Contains("Not found", StringComparison.OrdinalIgnoreCase))
                        return null;

                    _logger.LogWarning("QRZ API error for {Callsign}: {Error}", callsign, sessionError);
                    return null;
                }

                var callEl = doc.Descendants(ns + "Callsign").FirstOrDefault();
                if (callEl is null) return null;

                var fname = callEl.Element(ns + "fname")?.Value;
                var addr2 = callEl.Element(ns + "addr2")?.Value; // city/location

                if (string.IsNullOrWhiteSpace(fname) && string.IsNullOrWhiteSpace(addr2))
                    return null;

                return new QrzInfo(fname?.Trim(), addr2?.Trim());
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning("QRZ HTTP error for {Callsign}: {Message}", callsign, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("QRZ lookup failed for {Callsign}: {Message}", callsign, ex.Message);
                return null;
            }
        }

        return null;
    }

    private async Task<string?> EnsureSessionKeyAsync(QrzSettings settings)
    {
        if (_sessionKey is not null) return _sessionKey;

        await _loginLock.WaitAsync();
        try
        {
            if (_sessionKey is not null) return _sessionKey;

            if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
            {
                _logger.LogWarning("QRZ.com credentials not configured.");
                return null;
            }

            var url = $"{BaseUrl}?username={Uri.EscapeDataString(settings.Username)}&password={Uri.EscapeDataString(settings.Password)}&agent=MeshcomWebDesk";
            if (settings.LogRequests)
                _logger.LogInformation("QRZ >> GET {Url}", $"{BaseUrl}?username={Uri.EscapeDataString(settings.Username)}&password=***&agent=MeshcomWebDesk");
            var xml = await _http.GetStringAsync(url);
            if (settings.LogRequests)
                _logger.LogInformation("QRZ << {Response}", xml);
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://xmldata.qrz.com";

            var key = doc.Descendants(ns + "Key").FirstOrDefault()?.Value;
            if (!string.IsNullOrEmpty(key))
            {
                _sessionKey = key;
                _logger.LogInformation("QRZ.com session established.");
                return _sessionKey;
            }

            var error = doc.Descendants(ns + "Error").FirstOrDefault()?.Value;
            _logger.LogWarning("QRZ.com login failed: {Error}", error ?? "unknown error");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("QRZ.com login error: {Message}", ex.Message);
            return null;
        }
        finally
        {
            _loginLock.Release();
        }
    }
}

/// <summary>Basic QRZ.com callsign data (first name and city/location).</summary>
public sealed record QrzInfo(string? FirstName, string? Location);

/// <summary>An in-memory and on-disk cache entry that wraps <see cref="QrzInfo"/> with its fetch timestamp.</summary>
internal sealed record QrzCacheEntry(QrzInfo? Info, DateTime CachedAt);
