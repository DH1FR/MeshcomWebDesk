using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// TLS Telnet client connecting to the MeshCom node on port 2323.
/// Connect flow: TCP → SslStream handshake → cert pin check → password → read loop.
/// </summary>
public class TelnetService : IAsyncDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<TelnetService> _logger;

    private TcpClient?    _tcp;
    private SslStream?    _ssl;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public bool    IsConnected           { get; private set; }
    public bool    IsEnabled             => _settingsMonitor.CurrentValue.TelnetEnabled;
    public string? UnknownCertThumbprint { get; private set; }
    public string  LastLine              { get; private set; } = string.Empty;

    /// <summary>Ring-buffer of received output lines (newest last).</summary>
    public List<string> Lines { get; } = new(500);

    public event Action? OnChange;

    public TelnetService(IOptionsMonitor<MeshcomSettings> settings, ILogger<TelnetService> logger)
    {
        _settingsMonitor = settings;
        _logger          = logger;
    }

    public async Task ConnectAsync()
    {
        if (IsConnected) return;
        var s    = _settingsMonitor.CurrentValue;
        var host = s.DeviceIp;
        var port = s.TelnetPort;
        UnknownCertThumbprint = null;

        AppendLine($"[Verbinde mit {host}:{port} …]");
        OnChange?.Invoke();

        using var timeoutCts  = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // TCP connect with timeout
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port, timeoutCts.Token);

            _ssl = new SslStream(_tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: ValidateDeviceCert);

            // Use explicit TLS options:
            // - Allow TLS 1.0–1.3 so older node firmware is accepted
            // - Disable SNI (TargetHost = "") when connecting to an IP address,
            //   because many embedded TLS stacks drop the connection on unknown SNI
            bool isIpAddress = IPAddress.TryParse(host, out _);
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost                          = isIpAddress ? string.Empty : host,
                RemoteCertificateValidationCallback = ValidateDeviceCert,
#pragma warning disable SYSLIB0039  // TLS 1.0/1.1 are obsolete in .NET but needed for embedded devices
                EnabledSslProtocols                 = SslProtocols.Tls | SslProtocols.Tls11
                                                    | SslProtocols.Tls12 | SslProtocols.Tls13,
#pragma warning restore SYSLIB0039
            };
            await _ssl.AuthenticateAsClientAsync(sslOptions, timeoutCts.Token);

            _reader = new StreamReader(_ssl, Encoding.UTF8);
            _writer = new StreamWriter(_ssl, Encoding.UTF8) { AutoFlush = true };

            // Read first line from device with timeout
            var firstLine = await _reader.ReadLineAsync(timeoutCts.Token);
            _logger.LogDebug("Telnet first line: {Line}", firstLine);

            if (firstLine != null && firstLine.TrimStart().StartsWith("Password", StringComparison.OrdinalIgnoreCase))
            {
                // Password prompt – send password
                await _writer.WriteLineAsync(s.TelnetPassword);
                var authResult = await _reader.ReadLineAsync(timeoutCts.Token);
                _logger.LogDebug("Telnet auth result: {Result}", authResult);

                if (authResult != null && authResult.Contains("denied", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Telnet auth failed: {Result}", authResult);
                    await DisposeConnectionAsync();
                    AppendLine($"❌ Zugang verweigert: {authResult}");
                    OnChange?.Invoke();
                    return;
                }
                if (!string.IsNullOrEmpty(authResult))
                    AppendLine(authResult);
            }
            else
            {
                // No password – firstLine is the banner
                if (!string.IsNullOrEmpty(firstLine))
                    AppendLine(firstLine);
            }

            IsConnected = true;
            AppendLine($"[Verbunden mit {host}:{port}]");
            _cts = new CancellationTokenSource();
            _ = ReadLoopAsync(_cts.Token);
            _logger.LogInformation("Telnet connected to {Host}:{Port}", host, port);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Telnet connect timed out to {Host}:{Port}", host, port);
            await DisposeConnectionAsync();
            AppendLine($"❌ Timeout beim Verbinden mit {host}:{port} (10s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telnet connect failed");
            await DisposeConnectionAsync();
            AppendLine($"❌ Verbindungsfehler: {ex.Message}");
        }
        OnChange?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        await DisposeConnectionAsync();
        IsConnected = false;
        LastLine    = string.Empty;
        AppendLine("[Getrennt]");
        OnChange?.Invoke();
        _logger.LogInformation("Telnet disconnected");
    }

    public async Task SendLineAsync(string line)
    {
        if (!IsConnected || _writer == null) return;
        try { await _writer.WriteLineAsync(line); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telnet send failed");
            await DisconnectAsync();
        }
    }

    private bool ValidateDeviceCert(object sender, X509Certificate? cert,
        X509Chain? chain, SslPolicyErrors errors)
    {
        if (cert == null) return false;

        var raw         = cert.GetRawCertData();
        var hash        = SHA256.HashData(raw);
        var fingerprint = string.Join(":", hash.Select(b => b.ToString("X2")));

        var knownThumbprint = _settingsMonitor.CurrentValue.TelnetCertThumbprint
            .Replace(":", "").Replace(" ", "").ToUpperInvariant();

        if (string.IsNullOrEmpty(knownThumbprint))
        {
            // First-connect: accept any cert (self-signed, IP-addressed), expose fingerprint
            UnknownCertThumbprint = fingerprint;
            _logger.LogWarning("Telnet: first-connect cert fingerprint {Fp} – awaiting user confirmation", fingerprint);
            return true; // accept unconditionally, user will confirm fingerprint
        }

        // Pinned: compare SHA-256 fingerprints (ignore colons/spaces/case)
        var incoming = fingerprint.Replace(":", "").ToUpperInvariant();
        if (incoming == knownThumbprint)
        {
            _logger.LogDebug("Telnet cert fingerprint OK");
            return true;
        }

        _logger.LogError("Telnet cert MISMATCH! Expected {E}, got {G} – connection refused", knownThumbprint, incoming);
        return false;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;
                LastLine = line;
                AppendLine(line);
                OnChange?.Invoke();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "Telnet read loop ended"); }
        if (IsConnected) await DisconnectAsync();
    }

    private void AppendLine(string line)
    {
        lock (Lines)
        {
            if (Lines.Count >= 500) Lines.RemoveAt(0);
            Lines.Add(line);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_writer != null) { try { await _writer.DisposeAsync(); } catch { } _writer = null; }
        if (_reader != null) { try { _reader.Dispose(); } catch { } _reader = null; }
        if (_ssl    != null) { try { await _ssl.DisposeAsync(); } catch { } _ssl = null; }
        if (_tcp    != null) { try { _tcp.Dispose(); } catch { } _tcp = null; }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        await DisposeConnectionAsync();
    }
}
