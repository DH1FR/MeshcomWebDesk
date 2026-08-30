using System.Globalization;
using System.Text.RegularExpressions;

namespace MeshcomWebDesk.Services.Kiss;

public enum AprsInfoKind { Unknown, Position, Message }

/// <summary>Uncompressed APRS position parsed from an info field.</summary>
public sealed record AprsPosition(
    double Lat, double Lon, char SymbolTable, char SymbolCode, string Comment);

/// <summary>APRS text message parsed from a <c>:ADDRESSEE :text</c> info field.</summary>
public sealed record AprsTextMessage(string Addressee, string Text, string? SequenceNumber, bool IsAck);

/// <summary>
/// Result of parsing a KISS/AX.25 info field, including the MeshCom comment
/// extensions (<c>/B= /A= /N /P= /H= /T= /R=</c>). See firmware
/// <c>kiss_tcp_protocol.md</c> §3 and <c>docs/kiss-mode-analysis.md</c> §5.5.
/// </summary>
public sealed class AprsInfoResult
{
    public AprsInfoKind Kind { get; init; }
    public AprsPosition? Position { get; init; }
    public AprsTextMessage? Message { get; init; }

    // ── MeshCom comment extensions (position frames) ──
    public int?    BatteryPercent { get; set; }
    public int?    AltitudeMeters { get; set; }   // from "/A=" (feet in the comment)
    public int?    NeighbourCount { get; set; }
    public double? Pressure       { get; set; }
    public double? Humidity       { get; set; }
    public double? Temperature    { get; set; }
    public string? RelayNodeList  { get; set; }
}

/// <summary>
/// Parser for the APRS information field carried in KISS data frames. Fail-soft:
/// unrecognised input returns <see cref="AprsInfoKind.Unknown"/>.
/// </summary>
public static partial class AprsInfo
{
    public static AprsInfoResult Parse(string info)
    {
        if (string.IsNullOrEmpty(info))
            return new AprsInfoResult { Kind = AprsInfoKind.Unknown };

        char c0 = info[0];
        return c0 switch
        {
            ':' => ParseMessage(info),
            '!' or '=' => ParsePositionWithExtras(info.AsSpan(1)),
            '@' or '/' when info.Length > 8 => ParsePositionWithExtras(SkipTimestamp(info.AsSpan(1))),
            _ => new AprsInfoResult { Kind = AprsInfoKind.Unknown },
        };
    }

    private static ReadOnlySpan<char> SkipTimestamp(ReadOnlySpan<char> s) =>
        s.Length >= 7 ? s[7..] : s;   // "DDHHMMz" / "HMS" style, 7 chars

    // ── Message ──────────────────────────────────────────────────────────

    /// <summary>Pure APRS ack/rej body, e.g. "ack220".</summary>
    [GeneratedRegex(@"^(?:ack|rej)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AckBodyPattern();

    /// <summary>
    /// MeshCom inline ack/rej: the message text is "&lt;target&gt; :ack220" or
    /// "&lt;target&gt;:ack220" (mirrors <c>MeshcomUdpService.AckPattern</c>). Group 1 = target
    /// callsign, group 2 = sequence number.
    /// </summary>
    [GeneratedRegex(@"^(\S+?)\s*:(?:ack|rej)(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex InlineAckPattern();

    [GeneratedRegex(@"\{([A-Za-z0-9]+)\s*$")]
    private static partial Regex TrailingSeqPattern();

    private static AprsInfoResult ParseMessage(string info)
    {
        // ":ADDRESSEE :text"  – addressee is 9 chars, space-padded, then ':'
        if (info.Length < 11 || info[10] != ':')
            return new AprsInfoResult { Kind = AprsInfoKind.Unknown };

        var addressee = info[1..10].Trim();
        var text      = info[11..];

        string? seq = null;
        var seqMatch = TrailingSeqPattern().Match(text);
        if (seqMatch.Success)
        {
            seq  = seqMatch.Groups[1].Value;
            text = text[..seqMatch.Index];
        }

        var trimmed = text.Trim();
        bool isAck  = false;

        // MeshCom inline form: "DH1FR-1 :ack220" – the addressee field is the DM group,
        // the real ack target is inside the text. Take the target from the text.
        var inline = InlineAckPattern().Match(trimmed);
        if (inline.Success)
        {
            isAck     = true;
            seq       = inline.Groups[2].Value;
            addressee = inline.Groups[1].Value;
        }
        else if (AckBodyPattern().Match(trimmed) is { Success: true } bare)
        {
            isAck = true;
            seq   = bare.Groups[1].Value;
        }

        return new AprsInfoResult
        {
            Kind    = AprsInfoKind.Message,
            Message = new AprsTextMessage(addressee, text, seq, isAck),
        };
    }

    // ── Position ─────────────────────────────────────────────────────────

    private static AprsInfoResult ParsePositionWithExtras(ReadOnlySpan<char> s)
    {
        var pos = ParseUncompressedPosition(s);
        if (pos is null)
            return new AprsInfoResult { Kind = AprsInfoKind.Unknown };

        var result = new AprsInfoResult { Kind = AprsInfoKind.Position, Position = pos };
        ApplyCommentExtensions(result, pos.Comment);
        return result;
    }

    /// <summary>
    /// Parses <c>DDMM.mmN/DDDMM.mmE#comment</c> (8 + symtable + 9 + symcode + comment).
    /// </summary>
    private static AprsPosition? ParseUncompressedPosition(ReadOnlySpan<char> s)
    {
        if (s.Length < 19) return null;

        var latField = s[..8];        // DDMM.mmN
        char symTable = s[8];
        var lonField = s[9..18];      // DDDMM.mmE
        char symCode = s[18];
        var comment  = s.Length > 19 ? s[19..].ToString() : string.Empty;

        // MeshCom always uses the primary ('/') or alternate ('\') symbol table.
        if (symTable is not ('/' or '\\')) return null;
        if (!TryParseLatLon(latField, degDigits: 2, out double lat, "NS")) return null;
        if (!TryParseLatLon(lonField, degDigits: 3, out double lon, "EW")) return null;

        return new AprsPosition(lat, lon, symTable, symCode, comment);
    }

    private static bool TryParseLatLon(ReadOnlySpan<char> f, int degDigits, out double value, string hemis)
    {
        value = 0;
        // degDigits + "MM.mm" (5) + hemisphere (1)
        if (f.Length != degDigits + 6) return false;

        var degSpan = f[..degDigits];
        var minSpan = f.Slice(degDigits, 5);
        char hemi   = char.ToUpperInvariant(f[degDigits + 5]);
        if (hemis.IndexOf(hemi) < 0) return false;
        if (minSpan[2] != '.') return false;

        if (!int.TryParse(degSpan, out int deg)) return false;
        if (!double.TryParse(minSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double min)) return false;

        value = deg + min / 60.0;
        if (hemi is 'S' or 'W') value = -value;
        return true;
    }

    [GeneratedRegex(@"/B=(\d{1,3})")]                        private static partial Regex BattPattern();
    [GeneratedRegex(@"/A=(-?\d{1,7})")]                      private static partial Regex AltPattern();
    [GeneratedRegex(@"/N(\d{1,3})(?!\d)")]                   private static partial Regex NeighbourPattern();
    [GeneratedRegex(@"/P=(-?\d+(?:\.\d+)?)")]                private static partial Regex PressPattern();
    [GeneratedRegex(@"/H=(-?\d+(?:\.\d+)?)")]                private static partial Regex HumPattern();
    [GeneratedRegex(@"/T=(-?\d+(?:\.\d+)?)")]                private static partial Regex TempPattern();
    [GeneratedRegex(@"/R=([0-9;]+)")]                        private static partial Regex RelayPattern();

    private static void ApplyCommentExtensions(AprsInfoResult r, string comment)
    {
        if (string.IsNullOrEmpty(comment)) return;

        if (BattPattern().Match(comment) is { Success: true } b &&
            int.TryParse(b.Groups[1].Value, out int batt))
            r.BatteryPercent = Math.Clamp(batt, 0, 100);

        if (AltPattern().Match(comment) is { Success: true } a &&
            int.TryParse(a.Groups[1].Value, out int altFt))
            r.AltitudeMeters = (int)Math.Round(altFt * 0.3048);

        if (NeighbourPattern().Match(comment) is { Success: true } n &&
            int.TryParse(n.Groups[1].Value, out int nb))
            r.NeighbourCount = nb;

        if (PressPattern().Match(comment) is { Success: true } p &&
            double.TryParse(p.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double press))
            r.Pressure = press;

        if (HumPattern().Match(comment) is { Success: true } h &&
            double.TryParse(h.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hum))
            r.Humidity = hum;

        if (TempPattern().Match(comment) is { Success: true } t &&
            double.TryParse(t.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double temp))
            r.Temperature = temp;

        if (RelayPattern().Match(comment) is { Success: true } rel)
            r.RelayNodeList = rel.Groups[1].Value;
    }
}
