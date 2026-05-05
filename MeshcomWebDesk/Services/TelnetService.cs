using System.Net.Security;
using System.Net.Sockets;
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
        try
        {
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port);
            _ssl = new SslStream(_tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: ValidateDeviceCert);
            await _ssl.AuthenticateAsClientAsync(host);
            _reader = new StreamReader(_ssl, Encoding.UTF8);
            _writer = new StreamWriter(_ssl, Encoding.UTF8) { AutoFlush = true };
            var banner = await _reader.ReadLineAsync();
            _logger.LogDebug("Telnet banner: {Banner}", banner);
            await _writer.WriteLineAsync(s.TelnetPassword);
            var result = await _reader.ReadLineAsync();
            _logger.LogDebug("Telnet auth result: {Result}", result);
            if (result != null && result.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Telnet auth failed: {Result}", result);
                await DisposeConnectionAsync();
                LastLine = $"❌ {result}";
                OnChange?.Invoke();
                return;
            }
            IsConnected = true;
            LastLine    = result ?? string.Empty;
            AppendLine($"[Verbunden mit {host}:{port}]");
            _cts = new CancellationTokenSource();
            _ = ReadLoopAsync(_cts.Token);
            _logger.LogInformation("Telnet connected to {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telnet connect failed");
            await DisposeConnectionAsync();
            LastLine = $"❌ {ex.Message}";
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
            _logger.LogWarning("Telnet: unknown cert fingerprint {Fp}", fingerprint);
            return errors == SslPolicyErrors.None
                || errors == SslPolicyErrors.RemoteCertificateChainErrors;
        }
        var incoming = fingerprint.Replace(":", "").ToUpperInvariant();
        if (incoming == knownThumbprint) return true;
        _logger.LogError("Telnet cert mismatch! Expected {E}, got {G}", knownThumbprint, incoming);
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
