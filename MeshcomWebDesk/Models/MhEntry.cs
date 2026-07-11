using MeshcomWebDesk.Helpers;

namespace MeshcomWebDesk.Models;

public sealed record MhEntry(string Call, string Date, string Time, string Typ,
                              string Hardware, string Mod, string Rssi, string Snr,
                              string Dist, string Pl, string M, string Nc);

public static class MhParser
{
    public static List<MhEntry> Parse(List<string> lines, int fromIndex)
    {
        var result = new List<MhEntry>();
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

    public static string RssiClass(string rssiStr)
    {
        if (!int.TryParse(rssiStr, out var rssi)) return "";
        return rssi >= SignalHelper.RssiStrongDbm ? "mh-rssi-strong"
             : rssi >= SignalHelper.RssiGoodDbm   ? "mh-rssi-good"
             : rssi >= SignalHelper.RssiWeakDbm   ? "mh-rssi-weak"
             : "mh-rssi-bad";
    }
}
