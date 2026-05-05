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

public class TelnetService : IAsyncDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<TelnetService> _logger;

    private TcpClient?    _tcp;
    private SslStream?    _ssl;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public bool    IsConnected           { get; private set; }
    public bool    IsEnabled             => _settingsMonitor.CurrentValue.TelnetEnabled;
    public string? UnknownCertThumbprint { get; set; }
    public string  LastLine              { get; private set; } = string.Empty;
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

        AppendLine($"[Verbinde mit {host}:{port}...]");
        OnChange?.Invoke();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port, timeoutCts.Token);

            _ssl = new SslStream(_tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: ValidateDeviceCert);

            // ESP32 mbedTLS only supports RSA key exchange cipher suites with its self-signed RSA cert.
            // .NET 10 on Windows (SChannel) offers ECDHE/DHE first which causes the handshake to fail
            // with "ClientKeyExchange failed in DHM/ECD" on the device side.
            // CipherSuitesPolicy selects only RSA-based suites (no DHE/ECDHE key exchange).
            CipherSuitesPolicy? cipherPolicy = null;
            try
            {
                cipherPolicy = new CipherSuitesPolicy(new[]
                {
                    TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA,
                    TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA,
                    TlsCipherSuite.TLS_RSA_WITH_AES_128_CBC_SHA256,
                    TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256,
                    TlsCipherSuite.TLS_RSA_WITH_AES_128_GCM_SHA256,
                    TlsCipherSuite.TLS_RSA_WITH_AES_256_GCM_SHA384,
                });
            }
            catch (PlatformNotSupportedException)
            {
                // CipherSuitesPolicy is not supported on this platform (Windows <10.0.20348)
                // Fall back to default – may fail if server rejects ECDHE
                _logger.LogWarning("CipherSuitesPolicy not supported on this platform, using defaults");
            }

#pragma warning disable SYSLIB0039
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost                          = "meshcom",
                RemoteCertificateValidationCallback = ValidateDeviceCert,
                EnabledSslProtocols                 = System.Security.Authentication.SslProtocols.Tls12,
                CipherSuitesPolicy                  = cipherPolicy,
                CertificateRevocationCheckMode      = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
            };
#pragma warning restore SYSLIB0039
            await _ssl.AuthenticateAsClientAsync(sslOptions, timeoutCts.Token);

            _writer = new StreamWriter(_ssl, Encoding.UTF8) { AutoFlush = true };

            // Server sends "Password: " WITHOUT newline – use pause-based read
            var firstLine = await ReadUntilNewlineOrPauseAsync(timeoutCts.Token);
            _logger.LogDebug("Telnet first line: '{Line}'", firstLine);

            if (firstLine.TrimStart().StartsWith("Password", StringComparison.OrdinalIgnoreCase))
            {
                await _writer.WriteLineAsync(s.TelnetPassword);
                var authResult = await ReadUntilNewlineOrPauseAsync(timeoutCts.Token);
                _logger.LogDebug("Telnet auth result: '{Result}'", authResult);

                if (authResult.Contains("denied", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Telnet auth failed: {Result}", authResult);
                    await DisposeConnectionAsync();
                    AppendLine($"Zugang verweigert: {authResult}");
                    OnChange?.Invoke();
                    return;
                }
                if (!string.IsNullOrEmpty(authResult))
                    AppendLine(authResult);
            }
            else
            {
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
            AppendLine($"Timeout beim Verbinden mit {host}:{port} (10s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telnet connect failed");
            await DisposeConnectionAsync();
            AppendLine($"Verbindungsfehler: {ex.Message}");
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
            UnknownCertThumbprint = fingerprint;
            _logger.LogWarning("Telnet: first-connect cert fingerprint {Fp}", fingerprint);
            return true;
        }
        var incoming = fingerprint.Replace(":", "").ToUpperInvariant();
        if (incoming == knownThumbprint) return true;
        _logger.LogError("Telnet cert MISMATCH! Expected {E}, got {G}", knownThumbprint, incoming);
        return false;
    }

    private async Task<string> ReadUntilNewlineOrPauseAsync(CancellationToken ct)
    {
        var sb  = new StringBuilder();
        var buf = new byte[1];
        while (!ct.IsCancellationRequested)
        {
            using var pauseCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            using var linked   = CancellationTokenSource.CreateLinkedTokenSource(ct, pauseCts.Token);
            try
            {
                int n = await _ssl!.ReadAsync(buf.AsMemory(0, 1), linked.Token);
                if (n == 0) break;
                char c = (char)buf[0];
                if (c == '\n') break;
                if (c != '\r') sb.Append(c);
            }
            catch (OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                break; // 300ms pause = prompt without newline
            }
        }
        return sb.ToString();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var sb  = new StringBuilder();
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested && _ssl != null)
            {
                int n = await _ssl.ReadAsync(buf.AsMemory(), ct);
                if (n == 0) break;
                var text = Encoding.UTF8.GetString(buf, 0, n);
                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        var line = sb.ToString();
                        if (line.Length > 0) { LastLine = line; AppendLine(line); OnChange?.Invoke(); }
                        sb.Clear();
                    }
                    else if (c != '\r')
                    {
                        sb.Append(c);
                    }
                }
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
        if (_ssl    != null) { try { await _ssl.DisposeAsync(); } catch { } _ssl = null; }
        if (_tcp    != null) { try { _tcp.Dispose(); } catch { } _tcp = null; }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        await DisposeConnectionAsync();
    }
}