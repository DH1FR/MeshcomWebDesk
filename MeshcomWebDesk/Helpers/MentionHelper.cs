using System.Text.RegularExpressions;

namespace MeshcomWebDesk.Helpers;

/// <summary>
/// Recognises "@CALLSIGN" mentions of our own callsign(s) in a message text.
/// <para>
/// Rules (case-insensitive, whole callsign only):
/// <list type="bullet">
/// <item><c>@DH1FR-4</c> counts when <c>DH1FR-4</c> is one of our callsigns.</item>
/// <item><c>@DH1FR</c> (no SSID) counts for every callsign with that base, whatever its SSID.</item>
/// <item><c>@DH1FR-44</c> and <c>@DH1FR-1</c> do not count for <c>DH1FR-4</c>: another SSID can be another
///   device, so only our exact SSID – or none – is taken as "addressed to us".</item>
/// <item>No "@" in front (<c>DH1FR-4</c>), an "@" glued to a word (<c>dh1fr@darc.de</c>) and foreign
///   callsigns never count.</item>
/// </list>
/// </para>
/// </summary>
public static class MentionHelper
{
    // "@" must not follow a letter/digit ("mail@host") or another "@"/"/"; the callsign is letters, digits and "/"
    // (portable suffix); an optional "-SSID" follows. The end must not continue the callsign: no further letter,
    // digit or "/" (so "@DH1FR/P" is a different station), and no "-<digit>" left over (so "@DH1FR-444" is not
    // read as "@DH1FR" plus noise).
    private static readonly Regex MentionRx = new(
        @"(?<![A-Za-z0-9/@])@([A-Za-z0-9/]+?)(?:-(\d{1,2}))?(?![A-Za-z0-9/]|-\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when <paramref name="text"/> mentions at least one of <paramref name="ownCallsigns"/>.</summary>
    public static bool MentionsAny(string? text, IEnumerable<string> ownCallsigns)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('@') < 0) return false;

        List<(string Base, int? Ssid)>? own = null;
        foreach (Match m in MentionRx.Matches(text))
        {
            own ??= ownCallsigns.Select(Split).Where(c => c.Base.Length > 0).ToList();
            if (own.Count == 0) return false;

            var mentionBase = m.Groups[1].Value;
            int? mentionSsid = m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var s) ? s : null;

            foreach (var (ownBase, ownSsid) in own)
            {
                if (!string.Equals(ownBase, mentionBase, StringComparison.OrdinalIgnoreCase)) continue;
                if (mentionSsid is null || mentionSsid == ownSsid) return true;
            }
        }
        return false;
    }

    private static (string Base, int? Ssid) Split(string callsign)
    {
        var c = (callsign ?? string.Empty).Trim();
        var dash = c.LastIndexOf('-');
        if (dash > 0 && int.TryParse(c[(dash + 1)..], out var ssid)) return (c[..dash], ssid);
        return (c, null);
    }
}
