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

public class TelnetService : IConsoleService, IAsyncDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<TelnetService> _logger;
    private readonly ConsoleLogService _consoleLog;

    private TcpClient?    _tcp;
    private SslStream?    _ssl;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    // Effective thumbprint used during the current/most recent connect attempt.
    // Set from ConnectAsync so ValidateDeviceCert (callback, no parameters) can access it.
    private string _activeCertThumbprint = string.Empty;

    public bool    IsConnected           { get; private set; }
    public bool    IsEnabled             => _settingsMonitor.CurrentValue.TelnetEnabled;
    /// <summary>Host to which the current connection was established. Empty when not connected.</summary>
    public string  ConnectedHost         { get; private set; } = string.Empty;
    /// <summary>
    /// Set when an unknown (first-connect) certificate is received.
    /// Callers (Settings page, Telnet page) can read this and offer a "Trust &amp; save" button.
    /// Reset to null at the start of each ConnectAsync call.
    /// </summary>
    public string? UnknownCertThumbprint { get; set; }
    public string  LastLine              { get; private set; } = string.Empty;
    public List<string> Lines { get; } = new(500);

    public event Action? OnChange;

    public TelnetService(IOptionsMonitor<MeshcomSettings> settings, ILogger<TelnetService> logger, ConsoleLogService consoleLog)
    {
        _settingsMonitor = settings;
        _logger          = logger;
        _consoleLog      = consoleLog;
    }

    /// <summary>Implements <see cref="IConsoleService.ConnectAsync"/> (legacy single-parameter overload).</summary>
    Task IConsoleService.ConnectAsync(string? hostOverride) =>
        ConnectAsync(hostOverride: hostOverride);

    /// <param name="hostOverride">IP or hostname to connect to; falls back to <c>settings.DeviceIp</c>.</param>
    /// <param name="certThumbprintOverride">
    ///   SHA-256 fingerprint of the expected node certificate (colon-separated hex).
    ///   Pass <c>null</c> or empty to fall back to <c>settings.TelnetCertThumbprint</c>.
    ///   Pass an empty string explicitly to force first-connect mode for a specific node.
    /// </param>
    /// <param name="passwordOverride">
    ///   Password to send after TLS handshake.  <c>null</c> = use <c>settings.TelnetPassword</c>.
    /// </param>
    public async Task ConnectAsync(
        string? hostOverride           = null,
        string? certThumbprintOverride = null,
        string? passwordOverride       = null)
    {
        if (IsConnected) return;
        var s    = _settingsMonitor.CurrentValue;
        var host = hostOverride ?? s.DeviceIp;
        var port = s.TelnetPort;
        UnknownCertThumbprint = null;

        // Resolve effective cert thumbprint:
        // - null  → legacy single-node: fall back to global setting
        // - ""    → multi-node first-connect: no known thumbprint, accept any cert
        // - "AA:" → multi-node: verify against this specific fingerprint
        _activeCertThumbprint = certThumbprintOverride ?? s.TelnetCertThumbprint;

        AppendLine($"[Verbinde mit {host}:{port}...]");
        OnChange?.Invoke();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port, timeoutCts.Token);

            _ssl = new SslStream(_tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: ValidateDeviceCert);

            // MeshCom node uses ECDHE-ECDSA key exchange (EC cert, X25519 temp key).
            // Restrict to ECDHE-ECDSA suites so .NET/SChannel does not offer incompatible suites first.
            CipherSuitesPolicy? cipherPolicy = null;
            try
            {
#pragma warning disable CA1416 // PlatformNotSupportedException is caught below for Windows
                cipherPolicy = new CipherSuitesPolicy(new[]
                {
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA,
                });
#pragma warning restore CA1416
            }
            catch (PlatformNotSupportedException)
            {
                // CipherSuitesPolicy not supported on this platform – use defaults
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

            // UTF8Encoding without BOM – BOM would corrupt the first write (e.g. password)
            _writer = new StreamWriter(_ssl, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

            // Read all initial lines from server; skip empty leading lines.
            // Server may send: "\nPassword: " (no trailing newline) or directly the welcome banner.
            string promptLine = string.Empty;
            for (int i = 0; i < 5; i++)
            {
                var line = await ReadUntilNewlineOrPauseAsync(timeoutCts.Token);
                _logger.LogDebug("Telnet init line {I}: '{Line}'", i, line);
                if (string.IsNullOrEmpty(line)) continue;
                promptLine = line;
                break;
            }

            if (promptLine.TrimStart().StartsWith("Password", StringComparison.OrdinalIgnoreCase))
            {
                // Use override password first, fall back to global setting
                var effectivePassword = passwordOverride ?? s.TelnetPassword;
                // Send password with explicit CRLF (same as PuTTY)
                await _writer.WriteAsync(effectivePassword + "\r\n");
                await _writer.FlushAsync();

                // Read server response – may be multi-line (welcome banner after success, or "Access denied")
                var authResponse = new System.Text.StringBuilder();
                for (int i = 0; i < 5; i++)
                {
                    var line = await ReadUntilNewlineOrPauseAsync(timeoutCts.Token);
                    _logger.LogDebug("Telnet auth response line {I}: '{Line}'", i, line);
                    if (!string.IsNullOrEmpty(line))
                        authResponse.AppendLine(line);
                }
                var authResult = authResponse.ToString();
                _logger.LogDebug("Telnet auth result: '{Result}'", authResult);

                if (authResult.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                    authResult.Contains("invalid", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Telnet auth failed: {Result}", authResult);
                    await DisposeConnectionAsync();
                    AppendLine($"Zugang verweigert: {authResult.Trim()}");
                    OnChange?.Invoke();
                    return;
                }
                if (!string.IsNullOrWhiteSpace(authResult))
                    AppendLine(authResult.Trim());
            }
            else
            {
                if (!string.IsNullOrEmpty(promptLine))
                    AppendLine(promptLine);
            }

            IsConnected = true;
            ConnectedHost = host;
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
        IsConnected   = false;
        ConnectedHost = string.Empty;
        LastLine      = string.Empty;
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

        // _activeCertThumbprint was set at the start of ConnectAsync from either
        // the node-specific override or the global settings value.
        var knownThumbprint = _activeCertThumbprint
            .Replace(":", "").Replace(" ", "").ToUpperInvariant();

        if (string.IsNullOrEmpty(knownThumbprint))
        {
            // First-connect mode: accept the cert and expose fingerprint for the UI
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
        if (!string.IsNullOrEmpty(ConnectedHost))
        {
            var s       = _settingsMonitor.CurrentValue;
            var enabled = s.ConsoleLogEnabled;
            if (!enabled && s.Nodes.Count > 0)
            {
                var node = s.Nodes.FirstOrDefault(n =>
                    string.Equals(n.DeviceIp, ConnectedHost, StringComparison.OrdinalIgnoreCase));
                enabled = node?.ConsoleLogEnabled ?? false;
            }
            if (enabled)
                _ = _consoleLog.WriteAsync(ConnectedHost, true, line);
            else
                _logger.LogWarning("ConsoleLog skipped – host='{Host}' GlobalFlag={Global} Nodes={NodeCount}",
                    ConnectedHost, s.ConsoleLogEnabled, s.Nodes.Count);
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