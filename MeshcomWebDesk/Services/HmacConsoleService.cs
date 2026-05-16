using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Console-Verbindung via TCP mit HMAC-SHA256-Challenge-Response-Authentifizierung.
/// Protokoll:
///   1. TCP-Verbindung aufbauen  →  host:port
///   2. Server sendet:           ←  "NONCE: &lt;32 Hex-Zeichen&gt;\r\n"
///   3. Client berechnet:             HMAC-SHA256(key=Passwort UTF-8, data=Nonce-Bytes)
///   4. Client sendet:           →  "&lt;64 Hex-Zeichen&gt;\r\n"
///   5. Server antwortet:        ←  "OK\r\n" + Banner  oder  "FAIL\r\n" + disconnect
///   6. Ab hier: bidirektionaler Klartextdatenstrom
/// </summary>
public class HmacConsoleService : IConsoleService, IAsyncDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<HmacConsoleService> _logger;

    private TcpClient?        _tcp;
    private StreamReader?     _reader;
    private StreamWriter?     _writer;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected { get; private set; }
    public List<string> Lines { get; } = new(500);
    public event Action? OnChange;

    public HmacConsoleService(
        IOptionsMonitor<MeshcomSettings> settingsMonitor,
        ILogger<HmacConsoleService> logger)
    {
        _settingsMonitor = settingsMonitor;
        _logger          = logger;
    }

    public async Task ConnectAsync(string? hostOverride = null)
    {
        await _lock.WaitAsync();
        try
        {
            if (IsConnected) return;

            var s    = _settingsMonitor.CurrentValue;
            var host = hostOverride ?? s.DeviceIp;
            var port = s.TelnetPort;           // Standard 2323

            _cts = new CancellationTokenSource();

            AppendLine($"[Verbinde mit {host}:{port} (HMAC)…]");
            OnChange?.Invoke();

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port, timeoutCts.Token);

            var stream = _tcp.GetStream();
            _reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
            _writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

            // ── Schritt 2: NONCE lesen ────────────────────────────────────────
            var nonceLine = await _reader.ReadLineAsync(_cts.Token);
            if (string.IsNullOrEmpty(nonceLine) || !nonceLine.StartsWith("NONCE:", StringComparison.OrdinalIgnoreCase))
            {
                AppendLine($"✗ Unerwartete Server-Antwort: {nonceLine}");
                await CleanupAsync();
                return;
            }

            var nonceHex = nonceLine.Split(' ')[1].Trim();
            if (nonceHex.Length != 32)
            {
                AppendLine($"✗ Ungültige Nonce-Länge ({nonceHex.Length} Zeichen, erwartet 32).");
                await CleanupAsync();
                return;
            }

            // ── Schritt 3: HMAC-SHA256 berechnen ─────────────────────────────
            var nonceBytes = Convert.FromHexString(nonceHex);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(s.TelnetPassword));
            var response   = Convert.ToHexString(hmac.ComputeHash(nonceBytes)).ToLower();

            // ── Schritt 4: Response senden ────────────────────────────────────
            await _writer.WriteLineAsync(response);

            // ── Schritt 5: OK / FAIL auswerten ───────────────────────────────
            var serverReply = await _reader.ReadLineAsync(_cts.Token);
            if (!string.Equals(serverReply?.Trim(), "OK", StringComparison.OrdinalIgnoreCase))
            {
                AppendLine("✗ Authentifizierung fehlgeschlagen (FAIL). Passwort prüfen.");
                await CleanupAsync();
                return;
            }

            IsConnected = true;
            AppendLine($"● Verbunden mit {host}:{port} (HMAC-TCP)");
            OnChange?.Invoke();

            // ── Schritt 6: bidirektionaler Lese-Loop ─────────────────────────
            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }
        catch (OperationCanceledException)
        {
            AppendLine("✗ Verbindungs-Timeout.");
            await CleanupAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HMAC-Console connect failed");
            AppendLine($"✗ Verbindungsfehler: {ex.Message}");
            await CleanupAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await CleanupAsync();
            AppendLine("[Getrennt]");
            OnChange?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SendLineAsync(string line)
    {
        if (_writer == null || !IsConnected) return;
        try
        {
            await _writer.WriteLineAsync(line);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HMAC-Console send failed");
            AppendLine($"✗ Senden fehlgeschlagen: {ex.Message}");
            await CleanupAsync();
            OnChange?.Invoke();
        }
    }

    // ── Private Hilfsmethoden ──────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;   // Verbindung geschlossen
                AppendLine(line);
                OnChange?.Invoke();
            }
        }
        catch (OperationCanceledException) { /* normal bei Disconnect */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HMAC-Console read loop ended");
            AppendLine($"[Verbindung unterbrochen: {ex.Message}]");
        }
        finally
        {
            await CleanupAsync();
            OnChange?.Invoke();
        }
    }

    private async Task CleanupAsync()
    {
        IsConnected = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_writer != null) { await _writer.DisposeAsync(); _writer = null; }
        _reader?.Dispose(); _reader = null;
        _tcp?.Dispose();    _tcp    = null;
    }

    private void AppendLine(string line)
    {
        Lines.Add(line);
        if (Lines.Count > 500) Lines.RemoveAt(0);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _lock.Dispose();
    }
}
