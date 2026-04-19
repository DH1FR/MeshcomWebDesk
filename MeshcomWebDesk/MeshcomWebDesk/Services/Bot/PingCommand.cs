using System.Text;
using MeshcomWebDesk.Models;
using MeshcomWebDesk.Services;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// Replies with a pong including signal quality (RSSI/SNR), relay route, and receive timestamp
/// so the sender can verify bot availability and assess link quality.
/// </summary>
public class PingCommand(LanguageService lang) : IBotCommand
{
    public string Name        => "ping";
    public string Description => lang.T("Bot-Erreichbarkeit und Signalqualität prüfen", "Check bot availability and signal quality", "Verifica disponibilità bot e qualità segnale", "Comprobar disponibilidad del bot y calidad de señal");

    public Task<string> ExecuteAsync(string[] args, string senderCallsign)
        => ExecuteAsync(args, senderCallsign, null);

    public Task<string> ExecuteAsync(string[] args, string senderCallsign, MeshcomMessage? context)
    {
        var sb = new StringBuilder();
        sb.Append(lang.T($"Pong! 👋 {senderCallsign}",
                         $"Pong! 👋 {senderCallsign}",
                         $"Pong! 👋 {senderCallsign}",
                         $"Pong! 👋 {senderCallsign}"));

        if (context != null)
        {
            // Signal quality
            if (context.Rssi.HasValue)
            {
                sb.Append($" | RSSI: {context.Rssi} dBm");
                if (context.Snr.HasValue)
                    sb.Append($", SNR: {context.Snr:F1} dB");
            }

            // Route / relay path and hop count
            if (!string.IsNullOrWhiteSpace(context.RelayPath))
            {
                var hops  = context.RelayPath.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var label = lang.T("Route", "Route", "Percorso", "Ruta");
                var hopLabel = lang.T("Hops", "hops", "hop", "saltos");
                sb.Append($" | {label} ({hops.Length} {hopLabel}): {context.RelayPath}");
            }

            // Receive timestamp as proxy for transit context
            var timeLabel = lang.T("Empfangen", "Received", "Ricevuto", "Recibido");
            sb.Append($" | {timeLabel}: {context.Timestamp:HH:mm:ss}");
        }

        return Task.FromResult(sb.ToString());
    }
}
