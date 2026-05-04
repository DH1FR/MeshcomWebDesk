using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

/// <summary>
/// Manages a single Telnet (raw TCP) client connection to the MeshCom node on port 23.
/// Receives lines from the node and exposes them via <see cref="OnDataReceived"/>.
/// Local echo is suppressed – the node echoes all input back.
/// CR+LF is sent for every line (required by MeshCom node, same as PuTTY "Implicit CR in every LF").
/// </summary>
public sealed class TelnetService : IDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _optMon;
    private readonly ILogger<TelnetService> _logger;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readTask;

    private const int TelnetPort = 23;
    private const int MaxLines = 500;
    private const int ReconnectDelayMs = 5_000;

    // ── public state ─────────────────────────────────────────────────────

    public bool IsConnected => _client?.Connected == true && _stream != null;
    public bool IsEnabled => _optMon.CurrentValue.TelnetEnabled;

    /// <summary>Ring-buffer of received output lines (newest last).</summary>
    public List<string> Lines { get; } = new(MaxLines);

    /// <summary>Raised on the thread-pool whenever a new line arrives or connection state changes.</summary>
    public event Action? OnChange;

    public TelnetService(IOptionsMonitor<MeshcomSettings> optMon, ILogger<TelnetService> logger)
    {
        _optMon = optMon;
        _logger = logger;

        // React to settings changes live
        _optMon.OnChange((settings, name) =>
        {
            if (!settings.TelnetEnabled)
            {
                Task.Run(DisconnectAsync);
            }
        });
    }

    // ── Connect / Disconnect ──────────────────────────────────────────────

    /// <summary>Starts the connection loop. Safe to call multiple times.</summary>
    public async Task ConnectAsync()
    {
        if (_cts != null) return; // already running

        _cts = new CancellationTokenSource();
        _readTask = RunAsync(_cts.Token);
        await Task.CompletedTask;
    }

    /// <summary>Disconnects and stops the background loop.</summary>
    public async Task DisconnectAsync()
    {
        if (_cts == null) return;
        await _cts.CancelAsync();
        try { await (_readTask ?? Task.CompletedTask); } catch { /* ignored */ }
        CleanupConnection();
        _cts = null;
        _readTask = null;
        OnChange?.Invoke();
    }

    // ── Send ──────────────────────────────────────────────────────────────

    /// <summary>Sends a line to the node. CR+LF is appended automatically.</summary>
    public async Task SendLineAsync(string text)
    {
        if (_stream == null || !IsConnected) return;
        try
        {
            // CR+LF: same as PuTTY "Implicit CR in every LF"
            var bytes = Encoding.UTF8.GetBytes(text + "\r\n");
            await _stream.WriteAsync(bytes);
            await _stream.FlushAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telnet send failed");
        }
    }

    // ── Background read loop ──────────────────────────────────────────────

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var ip = _optMon.CurrentValue.DeviceIp;
            try
            {
                _logger.LogInformation("Telnet connecting to {Ip}:{Port}", ip, TelnetPort);
                _client = new TcpClient { NoDelay = true };
                await _client.ConnectAsync(ip, TelnetPort, ct);
                _stream = _client.GetStream();

                _logger.LogInformation("Telnet connected to {Ip}:{Port}", ip, TelnetPort);
                AppendLine($"[Verbunden mit {ip}:{TelnetPort}]");
                OnChange?.Invoke();

                var buffer = new byte[4096];
                var remainder = string.Empty;

                while (!ct.IsCancellationRequested)
                {
                    int read = await _stream.ReadAsync(buffer, ct);
                    if (read == 0) break; // server closed

                    // Decode, handle telnet IAC sequences (strip them)
                    var text = remainder + Encoding.UTF8.GetString(buffer, 0, read);
                    text = StripIac(text);

                    // Split into lines; keep incomplete last fragment
                    var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                    remainder = lines[^1]; // last element may be incomplete

                    for (int i = 0; i < lines.Length - 1; i++)
                    {
                        var line = lines[i];
                        if (line.Length > 0)
                            AppendLine(line);
                    }

                    OnChange?.Invoke();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telnet connection to {Ip} lost", ip);
                AppendLine($"[Verbindung getrennt – nächster Versuch in {ReconnectDelayMs / 1000} s]");
                OnChange?.Invoke();
            }
            finally
            {
                CleanupConnection();
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(ReconnectDelayMs, ct).ContinueWith(_ => { }); // swallow cancel
        }

        AppendLine("[Telnet getrennt]");
        OnChange?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void AppendLine(string line)
    {
        lock (Lines)
        {
            if (Lines.Count >= MaxLines)
                Lines.RemoveAt(0);
            Lines.Add(line);
        }
    }

    /// <summary>Strips Telnet IAC (0xFF) option negotiation sequences (3 bytes each).</summary>
    private static string StripIac(string text)
    {
        // IAC sequences are in the raw byte stream; since we decoded as UTF-8 they may appear
        // as U+00FF characters. Strip IAC DO/DONT/WILL/WONT (3-byte) and SB…SE blocks.
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\xFF') // IAC
            {
                if (i + 1 < text.Length)
                {
                    char cmd = text[i + 1];
                    if (cmd == '\xFF') { sb.Append('\xFF'); i += 2; continue; } // escaped 0xFF
                    if (cmd >= '\xFB' && cmd <= '\xFE') { i += 3; continue; }  // DO/DONT/WILL/WONT
                    if (cmd == '\xFA') // SB – skip until SE (0xF0)
                    {
                        int se = text.IndexOf('\xF0', i + 2);
                        i = se >= 0 ? se + 1 : text.Length;
                        continue;
                    }
                    i += 2; continue;
                }
                i++; continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private void CleanupConnection()
    {
        try { _stream?.Dispose(); } catch { /* ignored */ }
        try { _client?.Dispose(); } catch { /* ignored */ }
        _stream = null;
        _client = null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        CleanupConnection();
        _cts?.Dispose();
    }
}
