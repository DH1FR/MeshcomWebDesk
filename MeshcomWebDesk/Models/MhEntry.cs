using MeshcomWebDesk.Helpers;

namespace MeshcomWebDesk.Models;

public sealed record MhEntry(string Call, string Date, string Time, string Typ,
                              string Hardware, string Mod, string Rssi, string Snr,
                              string Dist, string Pl, string M, string Nc)
{
    // Extra keys of the firmware 4.40a "[MH] key=value" format (empty with the legacy ASCII table).
    public string Age  { get; init; } = "";
    public string Role { get; init; } = "";
    public string Nb   { get; init; } = "";
    public string Gw   { get; init; } = "";
    public string Hm   { get; init; } = "";
}

public static class MhParser
{
    private const string KvPrefix = "[MH]";

    /// <summary>Index of the start of the last --mheard response (new "[MH] window=" header or legacy table border).</summary>
    public static int FindLastStart(List<string> lines)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("/---") || (t.StartsWith(KvPrefix) && t.Contains("window="))) return i;
        }
        return 0;
    }

    /// <summary>True once the response from <paramref name="fromIndex"/> is complete: legacy table closed, or all announced "rows=" received.</summary>
    public static bool IsComplete(List<string> lines, int fromIndex, int parsedRows)
    {
        for (int i = fromIndex; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("\\")) return true;
            if (t.StartsWith(KvPrefix) && t.Contains("window="))
            {
                var rows = KeyValues(t).GetValueOrDefault("rows");
                return int.TryParse(rows, out var n) && parsedRows >= n;
            }
        }
        return false;
    }

    // "[MH] call=X 2026.10.03 18:00:55 typ=TXT ..." → key/value map; the bare date/time tokens after call= are kept as "date"/"time".
    private static Dictionary<string, string> KeyValues(string line)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tok in line.Substring(line.IndexOf(KvPrefix, StringComparison.Ordinal) + KvPrefix.Length)
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = tok.IndexOf('=');
            if (eq > 0) d[tok[..eq]] = tok[(eq + 1)..];
            else if (tok.Contains('.') && !d.ContainsKey("date")) d["date"] = tok;
            else if (tok.Contains(':') && !d.ContainsKey("time")) d["time"] = tok;
        }
        return d;
    }

    private static string Na(string? v) => string.IsNullOrEmpty(v) || v.StartsWith("NA", StringComparison.Ordinal) ? "-" : v;

    public static List<MhEntry> Parse(List<string> lines, int fromIndex)
    {
        var result = new List<MhEntry>();
        for (int i = fromIndex; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (!t.StartsWith(KvPrefix) || !t.Contains(" call=")) continue;
            var kv = KeyValues(t);
            if (!kv.TryGetValue("call", out var call) || string.IsNullOrEmpty(call)) continue;
            kv.TryGetValue("date", out var date); kv.TryGetValue("time", out var time);
            result.Add(new MhEntry(call, date ?? "", time ?? "", kv.GetValueOrDefault("typ") ?? "",
                                   kv.GetValueOrDefault("hw") ?? "", kv.GetValueOrDefault("mod") ?? "",
                                   StripUnit(kv.GetValueOrDefault("rssi"), "dBm"), StripUnit(kv.GetValueOrDefault("snr"), "dB"),
                                   Na(kv.GetValueOrDefault("dist")), kv.GetValueOrDefault("pl") ?? "", kv.GetValueOrDefault("m") ?? "",
                                   kv.GetValueOrDefault("ncnt") ?? "")
            {
                Age  = Na(StripUnit(kv.GetValueOrDefault("age"), "min")),
                Role = kv.GetValueOrDefault("role") ?? "",
                Nb   = kv.GetValueOrDefault("nb") ?? "",
                Gw   = kv.GetValueOrDefault("gw") ?? "",
                Hm   = Na(StripUnit(kv.GetValueOrDefault("hm"), "dB")),
            });
        }
        if (result.Count > 0) return result;

        bool inTable = false;
        bool headerSkipped = false;
        for (int i = fromIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!inTable) { if (line.TrimStart().StartsWith("/---")) inTable = true; continue; }
            if (line.TrimStart().StartsWith("\\")) break;
            if (line.Contains("|---")) continue;
            if (!headerSkipped) { headerSkipped = true; continue; }
            var cells = line.Split('|', StringSplitOptions.None).Select(c => c.Trim()).ToArray();
            if (cells.Length < 13 || string.IsNullOrEmpty(cells[1])) continue;
            result.Add(new MhEntry(cells[1], cells[2], cells[3], cells[4], cells[5],
                                   cells[6], cells[7], cells[8], cells[9], cells[10], cells[11], cells[12]));
        }
        return result;
    }

    private static string StripUnit(string? v, string unit) =>
        v is null ? "" : v.EndsWith(unit, StringComparison.Ordinal) ? v[..^unit.Length] : v;

    public static string RssiClass(string rssiStr)
    {
        if (!int.TryParse(rssiStr, out var rssi)) return "";
        return rssi >= SignalHelper.RssiStrongDbm ? "mh-rssi-strong"
             : rssi >= SignalHelper.RssiGoodDbm   ? "mh-rssi-good"
             : rssi >= SignalHelper.RssiWeakDbm   ? "mh-rssi-weak"
             : "mh-rssi-bad";
    }
}
