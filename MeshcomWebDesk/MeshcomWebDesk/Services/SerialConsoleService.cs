using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Options;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services;

public class SerialConsoleService : IConsoleService, IAsyncDisposable
{
    private readonly IOptionsMonitor<MeshcomSettings> _settingsMonitor;
    private readonly ILogger<SerialConsoleService> _logger;

    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected { get; private set; }
    public List<string> Lines { get; } = new(500);
    public event Action? OnChange;

    public SerialConsoleService(
        IOptionsMonitor<MeshcomSettings> settingsMonitor,
        ILogger<SerialConsoleService> logger)
    {
        _settingsMonitor = settingsMonitor;
        _logger = logger;
    }

    /// <summary>
    /// Returns available serial port names, deduplicated and sorted.
    /// <para>
    /// <see cref="SerialPort.GetPortNames"/> on Windows may return duplicate entries
    /// because it reads from multiple registry keys.  This wrapper applies
    /// <see cref="Enumerable.Distinct"/> and sorts the result.
    /// </para>
    /// </summary>
    public static string[] GetAvailablePorts() =>
        SerialPort.GetPortNames()
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                  .ToArray();

    public async Task ConnectAsync(string? hostOverride = null)
    {
        await _lock.WaitAsync();
        try
        {
            if (IsConnected) return;

            var settings = _settingsMonitor.CurrentValue;
            if (string.IsNullOrWhiteSpace(settings.SerialPortName))
            {
                AppendLine("⚠ Kein COM-Port konfiguriert.");
                return;
            }

            // .NET's SerialPort handles COM10+ correctly with the plain "COMx" name.
            // Do NOT prepend "\\.\\" – that causes "does not resolve to a valid serial port".
            var portName = settings.SerialPortName.Trim();

            _cts = new CancellationTokenSource();
            _port = new SerialPort(
                portName,
                settings.SerialBaudRate,
                Parity.None,
                8,
                StopBits.One)
            {
                Encoding = Encoding.UTF8,
                NewLine = "\n",
                ReadTimeout = 500,
                DtrEnable = true,
                RtsEnable = true
            };

            _port.Open();
            IsConnected = true;
            AppendLine($"● Verbunden mit {settings.SerialPortName} @ {settings.SerialBaudRate} Baud");
            OnChange?.Invoke();

            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Serial connect failed");
            AppendLine($"✗ Verbindungsfehler: {ex.Message}");
            IsConnected = false;
            OnChange?.Invoke();
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
            await DisposeConnectionAsync();
            AppendLine("○ Getrennt");
            OnChange?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SendLineAsync(string line)
    {
        if (_port is null || !IsConnected) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            await Task.Run(() => _port.BaseStream.Write(bytes, 0, bytes.Length));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Serial send failed");
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && _port is { IsOpen: true })
            {
                try
                {
                    var b = await Task.Run(() =>
                    {
                        try { return _port.ReadByte(); }
                        catch (TimeoutException) { return -2; }
                    }, ct);

                    if (b == -2) continue;   // timeout – retry
                    if (b < 0) break;        // port closed

                    var ch = (char)b;
                    if (ch == '\n')
                    {
                        var lineText = sb.ToString().TrimEnd('\r');
                        sb.Clear();
                        if (lineText.Length > 0)
                            AppendLine(lineText);
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Serial read error");
                    break;
                }
            }
        }
        finally
        {
            if (IsConnected)
            {
                IsConnected = false;
                AppendLine("○ Verbindung unterbrochen");
                OnChange?.Invoke();
            }
        }
    }

    private void AppendLine(string text)
    {
        lock (Lines)
        {
            if (Lines.Count >= 500) Lines.RemoveAt(0);
            Lines.Add(text);
        }
        OnChange?.Invoke();
    }

    private async Task DisposeConnectionAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        await Task.Run(() =>
        {
            try { _port?.Close(); } catch { /* ignore */ }
            _port?.Dispose();
        });
        _port = null;
        IsConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync();
        try { await DisposeConnectionAsync(); }
        finally { _lock.Release(); }
        _lock.Dispose();
    }
}
