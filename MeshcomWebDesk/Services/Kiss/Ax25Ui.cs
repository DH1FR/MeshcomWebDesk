using System.Text;

namespace MeshcomWebDesk.Services.Kiss;

/// <summary>
/// Decoded AX.25 UI frame (no FCS), as carried inside a KISS data frame.
/// </summary>
/// <param name="Dest">Destination address (the node's APRS-MC tocall – not meaningful, informational).</param>
/// <param name="Src">Origin callsign of the mesh packet, incl. SSID (e.g. "OE1ABC-7").</param>
/// <param name="Digipeaters">Relay callsigns from the address field, in order (the mesh path).</param>
/// <param name="Info">APRS information field, verbatim.</param>
public sealed record Ax25Frame(string Dest, string Src, IReadOnlyList<string> Digipeaters, string Info);

/// <summary>
/// Minimal AX.25 UI-frame codec for the KISS transport. Fail-closed: any malformed
/// input yields <c>null</c> on decode. See <c>docs/kiss-mode-analysis.md</c> §5.3 / §5.4
/// and firmware <c>kiss_tcp_protocol.md</c> §3 / §5.
/// </summary>
public static class Ax25Ui
{
    private const byte ControlUi = 0x03;
    private const byte PidNoLayer3 = 0xF0;
    private const int  AddrLen = 7;

    /// <summary>
    /// Decodes the payload of a KISS data frame (type 0x00, already de-escaped, WITHOUT
    /// the KISS type byte). Returns <c>null</c> when the bytes are not a well-formed
    /// AX.25 UI frame.
    /// </summary>
    public static Ax25Frame? Decode(ReadOnlySpan<byte> b)
    {
        // Minimum: dest(7) + src(7) + control(1) + pid(1)
        if (b.Length < 2 * AddrLen + 2) return null;

        var addrs = new List<(string Call, bool Last)>();
        int p = 0;
        while (p + AddrLen <= b.Length)
        {
            var seg = b.Slice(p, AddrLen);
            if (!TryDecodeAddress(seg, out var call, out var last)) return null;
            addrs.Add((call, last));
            p += AddrLen;
            if (last) break;
            if (addrs.Count > 10) return null; // dest + src + 8 digis max
        }

        if (addrs.Count < 2 || !addrs[^1].Last) return null;
        if (p + 2 > b.Length) return null;
        if (b[p] != ControlUi || b[p + 1] != PidNoLayer3) return null;
        p += 2;

        // MeshCom message/comment text is UTF-8 on the wire (emoji, umlauts …).
        var info = Encoding.UTF8.GetString(b[p..]);
        var digis = addrs.Skip(2).Select(a => a.Call).ToList();
        return new Ax25Frame(addrs[0].Call, addrs[1].Call, digis, info);
    }

    private static bool TryDecodeAddress(ReadOnlySpan<byte> a, out string call, out bool last)
    {
        call = string.Empty;
        last = false;
        if (a.Length != AddrLen) return false;

        Span<char> chars = stackalloc char[6];
        int n = 0;
        for (int i = 0; i < 6; i++)
        {
            int c = a[i] >> 1;
            if (c == ' ') continue;            // trailing pad
            if (c is < 0x21 or > 0x7E) return false;
            chars[n++] = (char)c;
        }
        if (n == 0) return false;

        int ssid = (a[6] >> 1) & 0x0F;
        last = (a[6] & 0x01) != 0;
        call = ssid > 0 ? $"{new string(chars[..n])}-{ssid}" : new string(chars[..n]);
        return true;
    }

    /// <summary>
    /// Builds a 2-address AX.25 UI frame (dest, src, control 0x03, PID 0xF0, info), no
    /// digipeaters, no FCS – ready to be wrapped in a KISS data frame for injection.
    /// Returns <c>null</c> when a base callsign exceeds 6 chars or the SSID is out of range.
    /// </summary>
    public static byte[]? Encode(string dest, string src, string info)
    {
        var d = EncodeAddress(dest, cBit: true,  last: false);
        var s = EncodeAddress(src,  cBit: false, last: true);
        if (d is null || s is null) return null;

        var infoBytes = Encoding.UTF8.GetBytes(info);
        var frame = new byte[AddrLen * 2 + 2 + infoBytes.Length];
        d.CopyTo(frame, 0);
        s.CopyTo(frame, AddrLen);
        frame[AddrLen * 2]     = ControlUi;
        frame[AddrLen * 2 + 1] = PidNoLayer3;
        infoBytes.CopyTo(frame, AddrLen * 2 + 2);
        return frame;
    }

    private static byte[]? EncodeAddress(string callWithSsid, bool cBit, bool last)
    {
        var dash = callWithSsid.IndexOf('-');
        var baseCall = (dash >= 0 ? callWithSsid[..dash] : callWithSsid).Trim().ToUpperInvariant();
        int ssid = 0;
        if (dash >= 0 && !int.TryParse(callWithSsid[(dash + 1)..], out ssid)) return null;
        if (baseCall.Length is 0 or > 6 || ssid is < 0 or > 15) return null;

        var a = new byte[AddrLen];
        for (int i = 0; i < 6; i++)
        {
            char c = i < baseCall.Length ? baseCall[i] : ' ';
            a[i] = (byte)(((byte)c) << 1);
        }
        a[6] = (byte)((cBit ? 0x80 : 0x00) | 0x60 | (ssid << 1) | (last ? 0x01 : 0x00));
        return a;
    }
}
