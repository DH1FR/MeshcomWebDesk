using System.Text;
using MeshcomWebDesk.Models;

namespace MeshcomWebDesk.Services.Bot;

/// <summary>
/// Tells the asker how this station received the command: directly or via which relay stations, and with
/// which signal. Unlike <see cref="PingCommand"/> the answer says outright whose signal the values are
/// (RSSI/SNR always belong to the LAST radio hop, i.e. the relay that was heard, not to the asker).
/// </summary>
public class RxCommand(LanguageService lang) : IBotCommand
{
    public string Name        => "rx";
    public string Description => lang.T("Wie ich dich empfange (Weg und Signal)", "How I receive you (path and signal)");

    public Task<string> ExecuteAsync(string[] args, string senderCallsign)
        => ExecuteAsync(args, senderCallsign, null);

    public Task<string> ExecuteAsync(string[] args, string senderCallsign, MeshcomMessage? context)
    {
        if (context is null)
            return Task.FromResult($"RX {senderCallsign}: {lang.T("keine Empfangsdaten", "no reception data")}");

        var relays     = RelaysOf(context.RelayPath);
        var viaGateway = string.Equals(context.SrcType, "udp", StringComparison.OrdinalIgnoreCase);

        // ── path ────────────────────────────────────────────────────────────
        string route;
        if (viaGateway)
            route = lang.T("über Gateway/Server", "via gateway/server");
        else if (relays.Count == 0)
            route = lang.T("direkt", "direct");
        else
            route = relays.Count == 1
                ? lang.TF("via {0} ({1} Hop)",  "via {0} ({1} hop)",  Shorten(relays), relays.Count)
                : lang.TF("via {0} ({1} Hops)", "via {0} ({1} hops)", Shorten(relays), relays.Count);

        // ── signal (of the last radio hop) ──────────────────────────────────
        string signal;
        if (!viaGateway && context.Rssi.HasValue)
        {
            var sb = new StringBuilder($"RSSI {context.Rssi} dBm");
            if (context.Snr.HasValue)
                sb.Append($", SNR {context.Snr.Value.ToString("F1", lang.NumberFormat)} dB");
            signal = relays.Count > 0
                ? lang.TF("von {0} gehört: {1}", "heard from {0}: {1}", relays[^1], sb.ToString())
                : sb.ToString();
        }
        else
        {
            signal = viaGateway
                ? lang.T("kein Funk-Signalwert", "no radio signal value")
                : lang.T("Signal unbekannt", "signal unknown");
        }

        return Task.FromResult($"RX {senderCallsign}: {route} | {signal} | {context.Timestamp:HH:mm:ss}");
    }

    /// <summary>
    /// The relay stations of a path "ORIGIN,RELAY1,RELAY2": the first entry is the origin, not a relay.
    /// Empty when the packet was heard directly (no path).
    /// </summary>
    internal static IReadOnlyList<string> RelaysOf(string? relayPath)
    {
        if (string.IsNullOrWhiteSpace(relayPath))
            return [];
        return relayPath.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Skip(1)
                        .ToList();
    }

    /// <summary>Keeps long paths short enough for one message: first, "…", last.</summary>
    private static string Shorten(IReadOnlyList<string> relays) =>
        relays.Count <= 3 ? string.Join(",", relays) : $"{relays[0]},…,{relays[^1]}";
}
