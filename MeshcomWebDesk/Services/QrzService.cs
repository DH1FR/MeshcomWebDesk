using System.Collections.Concurrent;
using System.Net.Http;
using System.Xml.Linq;
using MeshcomWebDesk.Models;
using Microsoft.Extensions.Options;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Queries the QRZ.com XML API for callsign information (first name and location).
/// Session keys are cached and refreshed automatically on expiry.
/// Callsign results are cached in memory for the lifetime of the application.
/// </summary>
public class QrzService
{
    private const string BaseUrl = "https://xmldata.qrz.com/xml/current/";

    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<QrzService> _logger;
    private readonly HttpClient _http;

    private string? _sessionKey;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private readonly ConcurrentDictionary<string, QrzInfo?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public QrzService(IOptionsMonitor<MeshcomSettings> settingsMonitor, ILogger<QrzService> logger)
    {
        _settingsMonitor = settingsMonitor;
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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

        if (_cache.TryGetValue(bare, out var cached))
            return cached;

        var result = await FetchAsync(bare, settings);
        _cache[bare] = result;
        return result;
    }

    /// <summary>Clears the in-memory callsign cache and the current session key.</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _sessionKey = null;
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
                var xml = await _http.GetStringAsync(url);
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
            var xml = await _http.GetStringAsync(url);
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
